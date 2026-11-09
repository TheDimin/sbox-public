"""Infer a perspective camera from pinned mesh and reference silhouette support."""
import json

import numpy as np
from PIL import Image

from test_source_oracle import MODEL, SOURCE


def fit():
    doc = json.loads((SOURCE / "DragonAttenuation.gltf").read_bytes())
    node = doc["nodes"][1]
    primitive, = doc["meshes"][node["mesh"]]["primitives"]
    accessor = doc["accessors"][primitive["attributes"]["POSITION"]]
    view = doc["bufferViews"][accessor["bufferView"]]
    blob = (SOURCE / doc["buffers"][view["buffer"]]["uri"]).read_bytes()
    assert (accessor["componentType"], accessor["type"]) == (5126, "VEC3")
    local = np.ndarray((accessor["count"], 3), dtype="<f4", buffer=blob,
                       offset=view.get("byteOffset", 0) + accessor.get("byteOffset", 0),
                       strides=(view.get("byteStride", 12), 4)).astype(float)
    # The pinned node rotates +90 degrees around X with uniform 0.25 scale.
    assert np.allclose(node["rotation"], [2 ** -.5, 0, 0, 2 ** -.5])
    vertices = local[:, [0, 2, 1]] * [node["scale"][0], -node["scale"][1], node["scale"][2]]
    vertices += node["translation"]
    with Image.open(MODEL / "screenshot/screenshot_large.png") as reference:
        width, height = reference.size
        r, g, b = np.asarray(reference.convert("RGB")).astype(int).transpose(2, 0, 1)
    # Isolate the yellow/orange dragon from the near-neutral backdrop.
    y, x = np.where((r - b > 25) & (r > g * 1.1) & (g > b * 1.2))
    angles = np.linspace(0, 2 * np.pi, 32, endpoint=False)
    directions = np.column_stack((np.cos(angles), np.sin(angles)))

    def support(points):
        return (points @ directions.T).max(axis=0)

    # Use extreme vertices only during optimization. Recheck with the full mesh.
    net = np.array(np.meshgrid(*([np.linspace(-1, 1, 9)] * 3))).reshape(3, -1).T
    extremes = np.concatenate([np.argmax(vertices @ chunk.T, axis=0) for chunk in np.array_split(net, 40)])
    proxy = vertices[np.unique(extremes)]
    expected = support(np.column_stack((x, y))[::5])

    def basis(camera):
        yaw, pitch = camera[3:5]
        forward = np.array([np.sin(yaw) * np.cos(pitch), np.sin(pitch), -np.cos(yaw) * np.cos(pitch)])
        right = np.array([np.cos(yaw), 0, np.sin(yaw)])
        return forward, right, np.cross(right, forward)

    def residual(camera, points=proxy):
        forward, right, up = basis(camera)
        relative = points - camera[:3]
        depth = relative @ forward
        if np.any(depth <= 0.01):
            return np.full(len(expected), 1e5)
        focal = np.exp(camera[5])
        pixels = np.column_stack((width / 2 + focal * (relative @ right) / depth,
                                  height / 2 - focal * (relative @ up) / depth))
        return support(pixels) - expected

    camera = np.array([0, .7, 5.7, 0, -np.arctan2(.2, 5.7), np.log(height / 2 / np.tan(np.deg2rad(22.5)))])
    rms = lambda r: float(np.sqrt(np.mean(r * r)))
    initial = rms(residual(camera, vertices))
    damping = 1.0
    for _ in range(120):
        difference = residual(camera)
        jacobian = np.column_stack([(residual(camera + axis) - difference) / 1e-5 for axis in np.eye(6) * 1e-5])
        step = np.linalg.solve(jacobian.T @ jacobian + damping * np.eye(6), -jacobian.T @ difference)
        trial = camera + step
        if rms(residual(trial)) < rms(difference):
            camera = trial
            damping = max(1e-6, damping * .5)
        else:
            damping = min(1e9, damping * 4)
    forward, _, _ = basis(camera)
    return {
        "space": "gltf-metres",
        "position": camera[:3].tolist(),
        "target": (camera[:3] + forward * 4).tolist(),
        "up": [0, 1, 0],
        "projection": "perspective",
        "verticalFov": float(np.rad2deg(2 * np.arctan(height / 2 / np.exp(camera[5])))),
        "referenceDimensions": [width, height],
        "initialSilhouetteSupportRmsPixels": initial,
        "fittedSilhouetteSupportRmsPixels": rms(residual(camera, vertices)),
        "scope": "Inferred from outer silhouette support; native camera and appearance need separate verification.",
    }


if __name__ == "__main__":
    print(json.dumps(fit(), indent=2))
