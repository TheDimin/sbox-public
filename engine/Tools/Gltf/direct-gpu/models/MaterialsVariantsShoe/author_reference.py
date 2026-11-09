#!/usr/bin/env python3
"""Bounded stdlib source oracle; never imports Sandbox.Gltf or renderer output."""
import hashlib
import json
from pathlib import Path
import struct
import sys

REVISION = "90d7ede14c7e280af263824604b427a1ca02cb66"
SAMPLES = [0, 1, 2, 31, 127, 511, 1023, 4095, 8191, 12000, 13538, 13539]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def container(path):
    data = path.read_bytes()
    if path.suffix == ".gltf":
        root = json.loads(data)
        binary = (path.parent / root["buffers"][0]["uri"]).read_bytes()
    else:
        magic, version, size = struct.unpack_from("<III", data)
        assert magic == 0x46546C67 and version == 2 and size == len(data)
        length, kind = struct.unpack_from("<II", data, 12)
        assert kind == 0x4E4F534A
        root = json.loads(data[20:20 + length])
        offset = 20 + length
        length, kind = struct.unpack_from("<II", data, offset)
        assert kind == 0x004E4942
        binary = data[offset + 8:offset + 8 + length]
    return data, root, binary


def author(checkout):
    model = checkout / "Temp/gltf-reference/glTF-Sample-Assets/Models/MaterialsVariantsShoe"
    result = {"revision": REVISION, "model": "MaterialsVariantsShoe", "license": "CC-BY-4.0", "credit": "Shopify, 2021", "forms": {}}
    for variant, filename in (("glTF", "MaterialsVariantsShoe.gltf"), ("glTF-Binary", "MaterialsVariantsShoe.glb")):
        path = model / variant / filename
        source, root, binary = container(path)
        assert root["extensionsUsed"] == ["KHR_materials_variants"]
        assert not root.get("extensionsRequired")
        primitive = root["meshes"][0]["primitives"][0]
        assert primitive["attributes"] == {"NORMAL": 0, "POSITION": 1, "TEXCOORD_0": 2}
        assert primitive["material"] == 0 and primitive["indices"] == 3 and primitive["mode"] == 4
        assert root["extensions"]["KHR_materials_variants"]["variants"] == [{"name": "midnight"}, {"name": "beach"}, {"name": "street"}]
        assert primitive["extensions"]["KHR_materials_variants"]["mappings"] == [{"material": i, "variants": [i]} for i in range(3)]
        # Independent literal hierarchy: node 2 -> node 1 -> mesh node 0.
        assert root["scenes"][0]["nodes"] == [2]
        assert root["nodes"][2]["children"] == [1] and root["nodes"][1]["children"] == [0]
        matrix = root["nodes"][2]["matrix"]
        assert matrix == [0.14893949, 0, 0, 0, 0, 0.14893949, 0, 0, 0, 0, 0.14893949, 0, 0.0016104877, 0.07590251, 0.0048509985, 1]
        form = {"source": f"{variant}/{filename}", "source_sha256": sha(source), "files": {}, "streams": {}, "samples": [], "images": []}
        dependencies = [path]
        if variant == "glTF":
            dependencies += [path.parent / root["buffers"][0]["uri"]]
            dependencies += [path.parent / image["uri"] for image in root["images"]]
        for dependency in dependencies:
            form["files"][str(dependency.relative_to(model))] = {"length": dependency.stat().st_size, "sha256": sha(dependency.read_bytes())}
        values = {}
        for semantic, accessor_index in {**primitive["attributes"], "INDICES": primitive["indices"]}.items():
            accessor = root["accessors"][accessor_index]
            view = root["bufferViews"][accessor["bufferView"]]
            components = {"VEC2": 2, "VEC3": 3, "SCALAR": 1}[accessor["type"]]
            size = components * 4
            offset, stride = accessor.get("byteOffset", 0), view.get("byteStride", size)
            view_data = binary[view.get("byteOffset", 0):view.get("byteOffset", 0) + view["byteLength"]]
            assert offset == 0 and stride == size
            values[semantic] = [struct.unpack_from("<" + ("I" if accessor["componentType"] == 5125 else "f") * components, view_data, offset + i * stride) for i in range(accessor["count"])]
            form["streams"][semantic] = {"count": accessor["count"], "component_type": accessor["componentType"], "components": components, "offset": offset, "stride": stride, "view_offset": view.get("byteOffset", 0), "length": len(view_data), "sha256": sha(view_data)}
        all_world = []
        for i, position in enumerate(values["POSITION"]):
            world = [position[k] * 0.14893949 + matrix[12 + k] for k in range(3)]
            engine = [world[2] / .0254, world[0] / .0254, world[1] / .0254]
            all_world.append(engine)
            if i in SAMPLES:
                normal = values["NORMAL"][i]
                length = sum(v * v for v in normal) ** .5
                form["samples"].append({"vertex": i, "position": list(position), "normal": list(normal), "uv": list(values["TEXCOORD_0"][i]), "world": world, "engine": engine, "engine_normal": [normal[2] / length, normal[0] / length, normal[1] / length]})
        form["bounds_min"] = [min(p[k] for p in all_world) for k in range(3)]
        form["bounds_max"] = [max(p[k] for p in all_world) for k in range(3)]
        for image in root["images"]:
            if "uri" in image:
                payload = (path.parent / image["uri"]).read_bytes()
            else:
                view = root["bufferViews"][image["bufferView"]]
                payload = binary[view["byteOffset"]:view["byteOffset"] + view["byteLength"]]
            form["images"].append({"sha256": sha(payload), "length": len(payload)})
        result["forms"][variant] = form
    left, right = result["forms"].values()
    assert left["samples"] == right["samples"] and left["images"] == right["images"]
    for semantic in left["streams"]:
        assert left["streams"][semantic]["sha256"] == right["streams"][semantic]["sha256"]
    return result


if __name__ == "__main__":
    checkout = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[6]
    output = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MaterialsVariantsShoe') / "reference.json"
    output.write_text(json.dumps(author(checkout), indent=2) + "\n")
    print(output)
