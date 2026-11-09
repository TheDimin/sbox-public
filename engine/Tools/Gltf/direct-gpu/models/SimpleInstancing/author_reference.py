"""Independent pinned-sample oracle: stdlib JSON/struct only, no Sandbox code.

The source arrays, not the upstream README's final-angle description, are
authoritative. Outputs stay in this model folder and contain no runtime receipt.
"""
import hashlib
import json
import math
from pathlib import Path
import struct

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
REVISION = "90d7ede14c7e280af263824604b427a1ca02cb66"
PINS = {
    "glTF/SimpleInstancing.gltf": "a01695630e6459c945826c7ff8dde74784de8279f60b9b62604980dc7486340e",
    "glTF/SimpleInstancing.bin": "29d38705c2a619f30374e80413be61f2e855dd9ec59c499201000aa53715a73c",
    "glTF-Binary/SimpleInstancing.glb": "1c9425627481346a8c226118f7000db7cd7f80198e9f54331f8b664d16726f3f",
}
OFFSETS = [0, 144, 432, 720, 2220, 4220]
COUNTS = [36, 24, 24, 125, 125, 125]
COMPONENTS = [1, 3, 3, 3, 4, 3]
NAMES = ["INDICES", "POSITION", "NORMAL", "TRANSLATION", "ROTATION", "SCALE"]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def quaternion_rotation(q):
    x, y, z, w = q
    length = math.sqrt(sum(v * v for v in q))
    x, y, z, w = (v / length for v in (x, y, z, w))
    return [[1 - 2*(y*y + z*z), 2*(x*y - z*w), 2*(x*z + y*w)],
            [2*(x*y + z*w), 1 - 2*(x*x + z*z), 2*(y*z - x*w)],
            [2*(x*z - y*w), 2*(y*z + x*w), 1 - 2*(x*x + y*y)]]


def transform(p, translation, quaternion, scale, normal=False):
    value = [p[c] / scale[c] if normal else p[c] * scale[c] for c in range(3)]
    rotation = quaternion_rotation(quaternion)
    result = [sum(rotation[r][c] * value[c] for c in range(3)) for r in range(3)]
    if normal:
        length = math.sqrt(sum(v*v for v in result))
        return [v/length for v in result]
    return [result[c] + translation[c] for c in range(3)]


def build():
    source_root = ROOT / "Temp/gltf-reference/glTF-Sample-Assets/Models/SimpleInstancing"
    files = {}
    for name, expected in PINS.items():
        data = (source_root/name).read_bytes()
        assert sha(data) == expected, f"Pinned input changed: {name}"
        files[name] = {"sha256": expected, "length": len(data)}
    document = json.loads((source_root/"glTF/SimpleInstancing.gltf").read_text())
    data = (source_root/"glTF/SimpleInstancing.bin").read_bytes()
    glb = (source_root/"glTF-Binary/SimpleInstancing.glb").read_bytes()
    assert struct.unpack_from("<3I", glb) == (0x46546c67, 2, len(glb))
    json_length, json_type = struct.unpack_from("<2I", glb, 12)
    assert json_type == 0x4e4f534a
    binary_document = json.loads(glb[20:20+json_length])
    binary_length, binary_type = struct.unpack_from("<2I", glb, 20+json_length)
    assert binary_type == 0x004e4942 and binary_length == len(data)
    assert glb[28+json_length:] == data
    comparable = json.loads(json.dumps(document))
    comparable["buffers"][0].pop("uri")
    assert comparable == binary_document, "Real upstream GLB metadata differs"
    accessors = []
    for index, (offset, count, components, name) in enumerate(zip(OFFSETS, COUNTS, COMPONENTS, NAMES)):
        accessor, view = document["accessors"][index], document["bufferViews"][index]
        component_type = 5125 if index == 0 else 5126
        assert accessor["bufferView"] == index and accessor["byteOffset"] == 0
        assert accessor["componentType"] == component_type and accessor["count"] == count
        assert view["buffer"] == 0 and view["byteOffset"] == offset
        assert view["byteLength"] == count * components * 4
        assert not accessor.get("normalized", False) and "byteStride" not in view
        fmt = "I" if index == 0 else "f"
        values = [list(struct.unpack_from("<"+fmt*components, data, offset+i*components*4)) for i in range(count)]
        accessors.append({"semantic": name, "view": index, "buffer": 0, "view_offset": offset,
                          "accessor_offset": 0, "stride": components*4, "component_type": component_type,
                          "components": components, "normalized": False, "count": count,
                          "length": view["byteLength"], "sha256": sha(data[offset:offset+view["byteLength"]]), "values": values})
    # Independently enforce the grid/scale pattern, avoiding mere source-versus-package agreement.
    for i in range(125):
        xyz = [i//25, (i//5)%5, i%5]
        assert accessors[3]["values"][i] == [2.5*v for v in xyz]
        assert accessors[5]["values"][i] == [1+v/4 for v in xyz]
    assert accessors[4]["values"][124] == [.5, .5, .5, .5]
    positions = accessors[1]["values"]
    normals = accessors[2]["values"]
    indices = [x[0] for x in accessors[0]["values"]]
    for a,b,c in zip(indices[0::3],indices[1::3],indices[2::3]):
        edge_a = [positions[b][k]-positions[a][k] for k in range(3)]
        edge_b = [positions[c][k]-positions[a][k] for k in range(3)]
        cross = [edge_a[1]*edge_b[2]-edge_a[2]*edge_b[1], edge_a[2]*edge_b[0]-edge_a[0]*edge_b[2], edge_a[0]*edge_b[1]-edge_a[1]*edge_b[0]]
        assert cross == normals[a] == normals[b] == normals[c]
    world = [transform(p, accessors[3]["values"][i], accessors[4]["values"][i], accessors[5]["values"][i]) for i in range(125) for p in positions]
    bounds = {"min": [min(v[c] for v in world) for c in range(3)], "max": [max(v[c] for v in world) for c in range(3)]}
    return {"revision": REVISION, "files": files, "accessors": accessors, "world_bounds": bounds,
            "final_rotation_degrees": 120, "source_node_world": "identity", "instances": 125,
            "default_material": {"base_color": [1,1,1,1], "metallic": 1, "roughness": 1, "alpha_mode": "OPAQUE", "double_sided": False}}


if __name__ == "__main__":
    ((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/SimpleInstancing') / "reference.json").write_text(json.dumps(build(), indent=2)+"\n")
