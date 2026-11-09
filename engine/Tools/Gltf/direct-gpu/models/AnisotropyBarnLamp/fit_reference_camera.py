#!/usr/bin/env python3
"""Fit an inferred camera to the copper silhouette in the pinned Khronos image."""
import argparse
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from audit_source import MODEL


def stream(doc, binary, index):
    accessor = doc["accessors"][index]
    view = doc["bufferViews"][accessor["bufferView"]]
    count = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[accessor["type"]]
    dtype = np.dtype({5123: "<u2", 5126: "<f4"}[accessor["componentType"]])
    return np.ndarray(
        (accessor["count"], count), dtype, buffer=binary,
        offset=accessor.get("byteOffset", 0) + view.get("byteOffset", 0),
        strides=(view.get("byteStride", count * dtype.itemsize), dtype.itemsize),
    )


def frame(parameters):
    pos = parameters[:3]
    yaw, pitch, roll, focal = parameters[3:]
    fwd = np.array([np.sin(yaw) * np.cos(pitch), np.sin(pitch), -np.cos(yaw) * np.cos(pitch)])
    right = np.cross(fwd, (0, 1, 0))
    right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    return pos, fwd, right * np.cos(roll) + up * np.sin(roll), up * np.cos(roll) - right * np.sin(roll), focal


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--diagnostic-dir", type=Path)
    args = parser.parse_args()
    source = MODEL / "glTF/AnisotropyBarnLamp.gltf"
    reference = MODEL / "screenshot/screenshot_Large.jpg"
    doc = json.loads(source.read_bytes())
    binary = (source.parent / doc["buffers"][0]["uri"]).read_bytes()
    copper = doc["meshes"][0]["primitives"][0]
    positions = stream(doc, binary, copper["attributes"]["POSITION"]).astype(np.float64)
    triangles = stream(doc, binary, copper["indices"]).reshape(-1, 3)
    original = Image.open(reference)
    width = 144
    height = round(width * original.height / original.width)
    rgb = np.asarray(original.resize((width, height), Image.Resampling.LANCZOS), dtype=np.int16)
    # This image has a near-neutral background and a transparent bulb. Only
    # chromatic copper is used in the fit; glass and filament are left unscored.
    target = (rgb[:, :, 0] - rgb[:, :, 2] > 10) & (rgb[:, :, 0] - rgb[:, :, 1] > 4)
    target_area = target.sum()
    assert 0.35 < target.mean() < 0.70, "Reference mask no longer identifies the copper silhouette."

    def silhouette(parameters):
        pos, fwd, right, up, focal = frame(parameters)
        relative = positions - pos
        z = relative @ fwd
        if z.min() < .05 or not 65 < focal < 600:
            return None
        xy = np.stack((width / 2 + focal * (relative @ right) / z, height / 2 - focal * (relative @ up) / z), axis=-1)
        xyz = np.rint(xy).astype(np.int32)
        a, b, c = np.transpose(xyz[triangles], axes=(1, 0, 2))
        delta1, delta2 = b - a, c - a
        # Winding allows us to drop the far side of closed copper surfaces.
        front = delta1[:, 0] * delta2[:, 1] < delta1[:, 1] * delta2[:, 0]
        coordinates = xyz[triangles[front]].reshape(-1, 6).tolist()
        mask = Image.new("1", (width, height))
        draw = ImageDraw.Draw(mask)
        for triangle in coordinates:
            draw.polygon(triangle, fill=1)
        return mask

    def cost(parameters):
        mask = silhouette(parameters)
        if mask is None:
            return 1e9
        return np.count_nonzero(np.asarray(mask) ^ target)

    # Fit from several plausible orbit yaw and focal values. A local simplex
    # avoids requiring scipy and accommodates the raster mask's step edges.
    def simplex(center, steps, iterations=155):
        cells = sorted([(cost(p), p) for p in [center, *[center + s for s in np.diag(steps)]]], key=lambda x: x[0])
        for _ in range(iterations):
            c = np.mean([p for _, p in cells[:-1]], axis=0)
            reflected = 2 * c - cells[-1][1]
            r = cost(reflected)
            if r < cells[0][0]:
                extended = 3 * c - 2 * cells[-1][1]
                e = cost(extended)
                cells[-1] = (e, extended) if e < r else (r, reflected)
            elif r < cells[-2][0]:
                cells[-1] = (r, reflected)
            else:
                p = .5 * c + .5 * (reflected if r < cells[-1][0] else cells[-1][1])
                if (d := cost(p)) < min(r, cells[-1][0]):
                    cells[-1] = d, p
                else:
                    anchor = cells[0][1]
                    cells = [cells[0], *[(cost(.5 * p + .5 * anchor), .5 * p + .5 * anchor) for _, p in cells[1:]]]
            cells.sort(key=lambda x: x[0])
        return cells[0]

    starts = [
        np.array([-.16, -.06, .36, .46, -.04, 0, 162]),
        np.array([-.21, -.05, .48, .5, -.07, 0, 220]),
        np.array([-.20, .00, .47, .55, -.19, 0, 230]),
    ]
    steps = np.array([.03, .015, .04, .09, .06, .02, 20])
    _, p = min((simplex(s, steps) for s in starts), key=lambda x: x[0])
    error, p = simplex(p, .2 * steps)
    mask = silhouette(p)
    actual = np.asarray(mask)
    pos, forward, _, up, focal = frame(p)
    to_engine = lambda v: (v[[2, 0, 1]] / .0254).tolist()
    result = {
        "reference": str(reference.relative_to(MODEL)),
        "referenceSha256": hashlib.sha256(reference.read_bytes()).hexdigest(),
        "imageSize": list(original.size),
        "fitSize": [width, height],
        "modelSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "cameraPositionInches": to_engine(pos),
        "cameraForward": forward[[2, 0, 1]].tolist(),
        "cameraUp": up[[2, 0, 1]].tolist(),
        "verticalFovDegrees": math.degrees(2 * math.atan(height / (2 * focal))),
        "copperSilhouetteIoU": float((target & actual).sum() / (target | actual).sum()),
        "copperReferenceMaskPixels": int(target_area),
        "copperMaskMismatchPixels": int(error),
        "sourceEnvironment": "Khronos Sample Viewer Studio Neutral; orientation, prefilter and exposure unknown",
        "sourceToneMapping": "ACES Filmic Tone Mapping (Hill)",
        "scope": "Inferred camera fit to colored copper silhouette only. Glass, material appearance and lighting are unscored.",
    }
    if args.diagnostic_dir:
        args.diagnostic_dir.mkdir(parents=True, exist_ok=True)
        mask.save(args.diagnostic_dir / "fitted-mask.png")
        Image.fromarray(target).save(args.diagnostic_dir / "reference-copper-mask.png")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
