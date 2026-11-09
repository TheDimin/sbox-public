#!/usr/bin/env python3
"""Read the pinned copper mesh/texture and derive independent anisotropy controls."""
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

from audit_source import MODEL
from fit_reference_camera import stream

OWNED = Path(__file__).resolve().parent
LANDMARKS = {
    "wall_plate_upper": (150, 139),
    "wall_plate_lower": (220, 417),
    "outer_shade_center": (575, 814),
    "outer_shade_right": (830, 862),
    "lettering": (470, 481),
    "knurled_ring": (530, 407),
}


def normalized(v):
    return v / np.linalg.norm(v)


def sample_linear(texture, uv):
    h, w, _ = texture.shape
    x, y = uv * (w, h) - .5
    u, v = math.floor(x), math.floor(y)
    dx, dy = x - u, y - v
    return (
        (1 - dy) * ((1 - dx) * texture[v % h, u % w] + dx * texture[v % h, (u + 1) % w])
        + dy * ((1 - dx) * texture[(v + 1) % h, u % w] + dx * texture[(v + 1) % h, (u + 1) % w])
    ) / 255


def sample_surface(position, tangent, normal, uv, indices, origin, ray):
    # Intersect the nearest forward-facing authored triangle. This is a
    # camera-ray calculation on the real source accessor layout.
    p0, p1, p2 = position[indices].transpose(1, 0, 2)
    d1, d2 = p1 - p0, p2 - p0
    cross = np.cross(ray, d2)
    det = (d1 * cross).sum(axis=1)
    good = det > 1e-10
    den = np.where(good, det, 1)
    offset = origin - p0
    b = (offset * cross).sum(axis=1) / den
    q = np.cross(offset, d1)
    c = (q @ ray) / den
    distance = (d2 * q).sum(axis=1) / den
    good &= (b >= 0) & (c >= 0) & (b + c <= 1) & (distance > 0)
    assert np.any(good), "Landmark does not touch the copper mesh"
    i = np.where(good, distance, np.inf).argmin()
    vertices = indices[i]
    bary = np.array([1 - b[i] - c[i], b[i], c[i]])
    n = normalized(bary @ normal[vertices])
    tx, ty, tz, sign = bary @ tangent[vertices]
    t = np.array([tx, ty, tz])
    t = normalized(t - n * (t @ n))
    return {
        "triangle": int(i),
        "sourceVertices": vertices.tolist(),
        "sourceBarycentric": bary.tolist(),
        "sourcePositionMeters": (origin + distance[i] * ray).tolist(),
        "uv": bary @ uv[vertices],
        "T": t,
        "B": np.sign(sign) * np.cross(n, t),
        "N": n,
        "handedness": int(np.sign(sign)),
    }


def effect(sample, roughness, amount, rotation, t, b):
    d = normalized(2 * sample[:2] - 1)
    s, c = math.sin(rotation), math.cos(rotation)
    direction = np.array([c * d[0] - s * d[1], s * d[0] + c * d[1]])
    strength = amount * sample[2]
    base_alpha = roughness * roughness
    tangent_alpha = base_alpha * (1 - strength * strength) + strength * strength
    return {
        "strength": float(strength),
        "directionTangentSpace": direction.tolist(),
        "axisWorld": (direction[0] * t + direction[1] * b)[[2, 0, 1]].tolist(),
        "alphaAlong": float(tangent_alpha),
        "alphaAcross": float(base_alpha),
    }


def main():
    source = MODEL / "glTF/AnisotropyBarnLamp.gltf"
    doc = json.loads(source.read_bytes())
    binary = (source.parent / doc["buffers"][0]["uri"]).read_bytes()
    copper = doc["meshes"][0]["primitives"][0]
    position, tangent, normal, uv = (
        stream(doc, binary, copper["attributes"][name]).astype(np.float64)
        for name in ("POSITION", "TANGENT", "NORMAL", "TEXCOORD_0")
    )
    indices = stream(doc, binary, copper["indices"]).reshape(-1, 3)
    copper_material = doc["materials"][copper["material"]]
    anisotropy = copper_material["extensions"]["KHR_materials_anisotropy"]

    def texture(info):
        image = doc["images"][doc["textures"][info["index"]]["source"]]
        path = source.parent / image["uri"]
        return np.asarray(Image.open(path).convert("RGB")), hashlib.sha256(path.read_bytes()).hexdigest()

    aniso, aniso_hash = texture(anisotropy["anisotropyTexture"])
    orm, orm_hash = texture(copper_material["pbrMetallicRoughness"]["metallicRoughnessTexture"])
    fit = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/AnisotropyBarnLamp') / "camera-fit.json").read_bytes())
    w, h = fit["imageSize"]
    focal = .5 * h / math.tan(math.radians(fit["verticalFovDegrees"]) * .5)
    origin = np.array(fit["cameraPositionInches"])[[1, 2, 0]] * .0254
    forward = np.array(fit["cameraForward"])[[1, 2, 0]]
    up = np.array(fit["cameraUp"])[[1, 2, 0]]
    right = np.cross(forward, up)
    landmarks = {}
    for name, (x, y) in LANDMARKS.items():
        ray = normalized(forward + (x + .5 - .5 * w) * right / focal + (.5 * h - y - .5) * up / focal)
        row = sample_surface(position, tangent, normal, uv, indices, origin, ray)
        sample = sample_linear(aniso, row["uv"])
        roughness = sample_linear(orm, row["uv"])[1]
        controls = {
            "authored": (sample, 1., 0.),
            "rotated90": (sample, 1., math.pi * .5),
            "zeroStrength": (sample, 0., 0.),
            "zeroBlue": (sample * (1, 1, 0), 1., 0.),
            "noTexture": (np.array([1., .5, 1.]), 1., 0.),
        }
        values = {key: effect(rgb, roughness, amount, angle, row["T"], row["B"])
                  for key, (rgb, amount, angle) in controls.items()}
        assert abs(np.dot(values["authored"]["axisWorld"], values["rotated90"]["axisWorld"])) < 1e-12
        assert values["zeroStrength"] == values["zeroBlue"]
        row = {k: v.tolist() if isinstance(v, np.ndarray) else v for k, v in row.items()}
        landmarks[name] = {**row, "referencePixel": [x, y], "sampledLinearRgb": sample.tolist(),
                           "roughness": float(roughness), "controls": values}
    print(json.dumps({
        "referenceSha256": fit["referenceSha256"], "sourceAnisotropyPngSha256": aniso_hash, "sourceOrmPngSha256": orm_hash,
        "scope": "Independent source-level, mip-0 bilinear landmarks at inferred copper camera rays; no GPU pixel or compressed-texture claim.",
        "landmarks": landmarks,
    }, indent=2))


if __name__ == "__main__":
    main()
