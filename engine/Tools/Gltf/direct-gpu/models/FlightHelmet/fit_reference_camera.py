#!/usr/bin/env python3
"""Infer a camera from source geometry and annotated edges of the pinned thumbnail."""
import hashlib
import json
from pathlib import Path

import numpy as np

root = Path(__file__).resolve().parents[6]
source = root / "Temp/gltf-reference/glTF-Sample-Assets/Models/FlightHelmet"
thumb = source / "screenshot/screenshot.jpg"
reference_sha256 = "ed5d7dd6e77c0f76a8e0878d61311ec2302a01be9bf636fc4e112197e1b607f1"
assert hashlib.sha256(thumb.read_bytes()).hexdigest() == reference_sha256
variant = source / "glTF-KTX-BasisU"
model = json.loads((variant / "FlightHelmet.gltf").read_bytes())
data = (variant / "FlightHelmet.bin").read_bytes()

# (y, left edge, right edge) in the 130x130 pinned JPEG. The hole beneath
# the helmet is excluded. Disjoint edges from the hanging strap are retained.
edges = np.array([
    [27, 62, 77], [30, 58, 80], [33, 56, 82], [39, 52, 85], [42, 52, 85],
    [45, 52, 85], [48, 53, 84], [54, 51, 83], [57, 52, 80], [60, 53, 79],
    [66, 55, 79], [72, 54, 81], [78, 53, 71], [84, 55, 68], [90, 56, 77],
    [96, 40, 93], [102, 40, 93], [105, 42, 88], [108, 50, 80], [111, 59, 72],
])


def vertices(primitive):
    accessor = model["accessors"][primitive["attributes"]["POSITION"]]
    view = model["bufferViews"][accessor["bufferView"]]
    assert accessor["componentType"] == 5126 and accessor["type"] == "VEC3"
    assert view["buffer"] == 0
    return np.ndarray(
        (accessor["count"], 3), dtype="<f4", buffer=data,
        offset=view.get("byteOffset", 0) + accessor.get("byteOffset", 0),
        strides=(view.get("byteStride", 12), 4),
    )


assert all(set(node) <= {"name", "mesh"} for node in model["nodes"])
positions = [vertices(mesh["primitives"][0]) for mesh in model["meshes"]]
points = np.unique(np.concatenate(positions), axis=0).astype(np.float64)
# A visible lens is left of the helmet center in the preview. Its center
# gives a left-vs-right viewpoint constraint independent of symmetric edges.
lens = np.mean(positions[5], axis=0).reshape(1, 3)


def project(points, eye, basis, focal):
    camera = (points - eye) @ basis
    return 65 + focal * camera[:, :2] / camera[:, 2:3]


def camera(parameters):
    azimuth, elevation, log_distance, log_focal, center_x, center_y = parameters
    direction = np.array([np.sin(azimuth) * np.cos(elevation), np.sin(elevation), np.cos(azimuth) * np.cos(elevation)])
    target = np.array([center_x, center_y, 0])
    eye = target + np.exp(log_distance) * direction
    right = np.cross(-direction, [0, 1, 0])
    right /= np.linalg.norm(right)
    down = np.cross(right, direction)
    return eye, target, np.column_stack((right, down, -direction)), np.exp(log_focal)


def score(parameters):
    eye, _, basis, focal = camera(parameters)
    x, y = project(points, eye, basis, focal).T
    outline = [(line.min(), line.max()) if line.size else (65, 65)
               for row in edges[:, 0] for line in [x[np.abs(y - row) <= .85]]]
    residual = np.array(outline) - edges[:, 1:]
    # Absolute outline disagreement matters, with tolerance for JPEG edges
    # and discrete projected vertices. Preserve upper/lower global extent.
    scale = abs(y.min() - 25.5) + abs(y.max() - 113)
    lens_x = project(lens, eye, basis, focal)[0, 0]
    lens_error = abs(lens_x - 59)
    return np.mean(np.abs(residual)) + .25 * scale + .15 * lens_error


def fit():
    best = (np.inf, None)
    for azimuth, elevation, fov in [(35, 14, 45), (50, 19, 42), (65, 25, 35), (35, 20, 30)]:
        focal = 65 / np.tan(np.radians(fov / 2))
        point = np.array([np.radians(azimuth), np.radians(elevation), np.log(.72 * focal / 89),
                          np.log(focal), 0., .35])
        step = np.array([.12, .08, .08, .08, .025, .025])
        value = score(point)
        for _ in range(7):
            for _ in range(4):
                for axis in range(len(point)):
                    trial = np.eye(len(point))[axis] * step[axis]
                    value, point = min([(value, point), (score(point + trial), point + trial),
                                        (score(point - trial), point - trial)], key=lambda item: item[0])
            step *= .56
        if value < best[0]:
            best = (value, point)
    return best


value, parameters = fit()
eye, target, _, focal = camera(parameters)
print(json.dumps({
    "referenceSha256": reference_sha256,
    "camera": {
        "space": "gltf-metres",
        "position": eye.tolist(),
        "target": target.tolist(),
        "up": [0, 1, 0],
        "projection": "perspective",
        "verticalFov": 2 * np.degrees(np.arctan(65 / focal)),
        "preset": "flight-helmet-thumbnail-fit",
    },
    "edgeRows": edges.tolist(),
    "referencePixelObjective": float(value),
    "scope": "Approximate no-roll perspective inferred from silhouette and lens location. Lighting and tone mapping are unknown.",
}, indent=2))
