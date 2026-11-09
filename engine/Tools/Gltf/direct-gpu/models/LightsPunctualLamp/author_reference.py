#!/usr/bin/env python3
"""Independently pin the light instances; no Sandbox reader/evaluator is used."""
import hashlib
import json
import math
import pathlib
import struct
import subprocess

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[4]
CORPUS = ROOT / "Temp/gltf-reference/glTF-Sample-Assets"
MODEL = CORPUS / "Models/LightsPunctualLamp"
REVISION = "90d7ede14c7e280af263824604b427a1ca02cb66"


def multiply(a, b):
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def local(node):
    if "matrix" in node:
        return [[node["matrix"][c * 4 + r] for c in range(4)] for r in range(4)]
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    n = math.sqrt(x*x + y*y + z*z + w*w)
    x, y, z, w = x/n, y/n, z/n, w/n
    scale = node.get("scale", [1, 1, 1])
    rotation = [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]
    translation = node.get("translation", [0, 0, 0])
    return [[rotation[r][c] * scale[c] for c in range(3)] + [translation[r]] for r in range(3)] + [[0, 0, 0, 1]]


def source_json(path):
    data = path.read_bytes()
    if path.suffix == ".gltf":
        return json.loads(data)
    magic, version, length = struct.unpack_from("<III", data)
    assert magic == 0x46546c67 and version == 2 and length == len(data)
    size, kind = struct.unpack_from("<II", data, 12)
    assert kind == 0x4e4f534a
    return json.loads(data[20:20+size])


def audit(root):
    nodes = root["nodes"]
    parents = {}
    for i, node in enumerate(nodes):
        for child in node.get("children", []):
            assert child not in parents
            parents[child] = i

    def world(index):
        matrix = local(nodes[index])
        return multiply(world(parents[index]), matrix) if index in parents else matrix

    selected = []

    def visit(index):
        assert index not in selected
        selected.append(index)
        for child in nodes[index].get("children", []):
            visit(child)

    for index in root["scenes"][root["scene"]].get("nodes", []):
        visit(index)
    definitions = root["extensions"]["KHR_lights_punctual"]["lights"]
    bindings = []
    for index in selected:
        reference = nodes[index].get("extensions", {}).get("KHR_lights_punctual")
        if reference is None:
            continue
        light_index = reference["light"]
        definition = definitions[light_index]
        matrix = world(index)
        meters = [matrix[r][3] for r in range(3)]
        bindings.append({"node": index, "light": light_index,
                         "type": definition["type"], "name": definition.get("name", ""),
                         "linear_color": definition.get("color", [1, 1, 1]),
                         "intensity": definition.get("intensity", 1),
                         "unit": "lux" if definition["type"] == "directional" else "candela",
                         "range_meters": definition.get("range"),
                         "position_meters": meters,
                         "engine_position_inches": [meters[c] / .0254 for c in [2, 0, 1]],
                         "source_column_world": matrix})
    return {"selected_nodes": selected, "mesh_count": len(root["meshes"]),
            "material_count": len(root["materials"]), "lights": bindings,
            "extensions_used": root.get("extensionsUsed", []),
            "extensions_required": root.get("extensionsRequired", [])}


def main():
    revision = subprocess.check_output(["git", "-C", str(CORPUS), "rev-parse", "HEAD"], text=True).strip()
    assert revision == REVISION
    files = {}
    for folder in ["glTF", "glTF-Binary"]:
        for path in sorted((MODEL / folder).iterdir()):
            data = path.read_bytes()
            files[path.relative_to(MODEL).as_posix()] = {"length": len(data), "sha256": hashlib.sha256(data).hexdigest()}
    reference = {"revision": revision, "model": "LightsPunctualLamp", "files": files, "variants": {}}
    for variant, filename in [("glTF", "LightsPunctualLamp.gltf"), ("glTF-Binary", "LightsPunctualLamp.glb")]:
        reference["variants"][variant] = audit(source_json(MODEL / variant / filename))
    ((pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/LightsPunctualLamp') / "reference.json").write_text(json.dumps(reference, indent=2) + "\n")
    print(json.dumps({"revision": revision, "files": len(files), "variants": {k: len(v["lights"]) for k, v in reference["variants"].items()}}))


if __name__ == "__main__":
    main()
