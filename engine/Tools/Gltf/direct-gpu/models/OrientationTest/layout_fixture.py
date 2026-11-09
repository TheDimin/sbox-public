"""Repack only OrientationTest storage, preserving its authored asset values.

Test-only adversarial fixture. Positions/normals share stride-32 views, with
distinct nonzero accessor offsets, poison padding, nonzero view offsets and
unchanged U8/U16 indices at accessor offset four. There is no fixed GltfVertex
in the tested path. Generated files live under Temp and retain upstream license.
"""

import argparse
import base64
import copy
import json
from pathlib import Path
import struct

import source_oracle as oracle


def interleave(root, buffers):
    changed = copy.deepcopy(root)
    arena = bytearray(b"\xcd" * 16)
    views = []

    def align():
        while len(arena) % 4:
            arena.append(0xCD)

    for mesh in changed["meshes"]:
        for primitive in mesh["primitives"]:
            if set(primitive["attributes"]) != {"POSITION", "NORMAL"}:
                raise ValueError("Orientation layout fixture requires the pinned attribute set")
            position_index = primitive["attributes"]["POSITION"]
            normal_index = primitive["attributes"]["NORMAL"]
            positions = oracle.read_accessor(root, buffers, position_index)
            normals = oracle.read_accessor(root, buffers, normal_index)
            if len(positions) != len(normals):
                raise ValueError("Pinned stream counts disagree")
            align()
            start = len(arena)
            for position, normal in zip(positions, normals):
                arena += b"\xcd" * 4
                arena += struct.pack("<fff", *position)
                arena += b"\xa5" * 4
                arena += struct.pack("<fff", *normal)
            view_index = len(views)
            views.append({"buffer": 0, "byteOffset": start, "byteLength": len(arena) - start, "byteStride": 32, "target": 34962})
            for index, offset in ((position_index, 4), (normal_index, 20)):
                accessor = changed["accessors"][index]
                if accessor["componentType"] != 5126 or accessor["type"] != "VEC3":
                    raise ValueError("Unexpected pinned FLOAT3 declaration")
                accessor["bufferView"] = view_index
                accessor["byteOffset"] = offset
            index = primitive["indices"]
            accessor = changed["accessors"][index]
            code, _ = oracle.COMPONENTS[accessor["componentType"]]
            indices = oracle.read_accessor(root, buffers, index)
            align()
            start = len(arena)
            arena += b"\xde\xad\xbe\xef"
            for value in indices:
                arena += struct.pack("<" + code, value[0])
            accessor["bufferView"] = len(views)
            accessor["byteOffset"] = 4
            views.append({"buffer": 0, "byteOffset": start, "byteLength": len(arena) - start, "target": 34963})
    changed["bufferViews"] = views
    changed["buffers"] = [{"byteLength": len(arena), "uri": "data:application/octet-stream;base64," + base64.b64encode(arena).decode("ascii")}]
    changed["asset"]["generator"] = "OrientationTest test-only storage repack; authored geometry/transforms/materials unchanged"
    return changed, [bytes(arena)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-out", type=Path, required=True)
    parser.add_argument("--oracle-out", type=Path, required=True)
    args = parser.parse_args()
    root, buffers = oracle.read_container(oracle.sample_variants()["glTF"])
    changed, changed_buffers = interleave(root, buffers)
    args.source_out.parent.mkdir(parents=True, exist_ok=True)
    args.source_out.write_text(json.dumps(changed, separators=(",", ":")) + "\n")
    description = oracle.describe(changed, changed_buffers)
    reference = {
        **description, "source": str(args.source_out.resolve()),
        "source_sha256": oracle.sha256(args.source_out.read_bytes()),
        "scope": "Synthetic storage mutation of pinned OrientationTest, not a separately supplied Khronos variant",
        "generator_sha256": oracle.sha256(Path(__file__).read_bytes()),
    }
    args.oracle_out.parent.mkdir(parents=True, exist_ok=True)
    args.oracle_out.write_text(json.dumps(reference, indent=2) + "\n")
    print(json.dumps({"source": str(args.source_out), "oracle": str(args.oracle_out), "geometry_sha256": reference["geometry_sha256"], "source_sha256": reference["source_sha256"]}))


if __name__ == "__main__":
    main()
