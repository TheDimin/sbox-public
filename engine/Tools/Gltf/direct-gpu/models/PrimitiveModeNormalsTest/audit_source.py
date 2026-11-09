#!/usr/bin/env python3
"""Bounded independent audit of this one pinned sample; no renderer/producer imports."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess

REVISION = "90d7ede14c7e280af263824604b427a1ca02cb66"
HASHES = {
    "PrimitiveModeNormalsTest.gltf": "432c9445f45c31d71b729c20804f91c04a9c60da67cab76c8fb61c2c70c83631",
    "Points.bin": "e1c8835ce934b6b4a9d028130babf442dfe1ec2d39c7e795bcbd4bde363eb363",
    "Lines.bin": "98febe022a429d8be2c7f628b89a4a1338354ef186c7b10b2f58f53296f1f8f1",
    "Triangles.bin": "99addf8a578a4088a9fdcd67a360c57be9f064355b21f53ad7bf30fffb6fc607",
    "Colors.bin": "20fd01539648e65f70bb9c1a31e1334ce0e4fb951436d818ebdf250ec3e74481",
    "Plane.bin": "68eb0aaba58264604648e828ba7536b3c52333659445200f56d90b23b05a2f36",
    "Labels.png": "6deb4b18af0594c4696b2afa2601ccba90b9a05d64322faf9ae608a6372edf15",
}


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def audit(checkout):
    corpus = checkout / "Temp/gltf-reference/glTF-Sample-Assets"
    revision = subprocess.check_output(["git", "-C", str(corpus), "rev-parse", "HEAD"], text=True).strip()
    require(revision == REVISION, "Corpus revision changed")
    model = corpus / "Models/PrimitiveModeNormalsTest"
    variants = sorted(p.name for p in model.iterdir() if p.is_dir() and p.name.startswith("glTF"))
    require(variants == ["glTF"], "Do not invent additional upstream variants")
    root = model / "glTF"
    blobs = {name: (root / name).read_bytes() for name in HASHES}
    for name, data in blobs.items():
        require(hashlib.sha256(data).hexdigest() == HASHES[name], "Pinned file changed: " + name)
    document = json.loads(blobs["PrimitiveModeNormalsTest.gltf"])
    require(document["scene"] == 0 and document["scenes"] == [{"nodes": [27]}], "Default scene changed")
    require(len(document["nodes"]) == 28 and len(document["meshes"]) == 25, "Scene instance inventory changed")
    require(not document.get("extensionsUsed") and not document.get("animations") and not document.get("skins"), "Static core-only source changed")
    vector_arrays = {name: list(struct.iter_unpack("<3f", blobs[name][:count * 12])) for name, count in (("Points.bin", 65536), ("Lines.bin", 65536), ("Triangles.bin", 205))}
    normal_lengths = {name: (min(math.sqrt(sum(x*x for x in p)) for p in points), max(math.sqrt(sum(x*x for x in p)) for p in points)) for name, points in vector_arrays.items()}
    require(all(abs(length - 1) < 2e-7 for limits in normal_lengths.values() for length in limits), "Authored normals are not nearly unit")
    indices = struct.unpack_from("<960H", blobs["Triangles.bin"], 2460)
    triangles = vector_arrays["Triangles.bin"]
    areas = []
    outward = []
    for start in range(0, len(indices), 3):
        a, b, c = (triangles[i] for i in indices[start:start+3])
        normal = cross(tuple(b[i] - a[i] for i in range(3)), tuple(c[i] - a[i] for i in range(3)))
        areas.append(sum(x*x for x in normal))
        outward.append(sum(normal[i] * (a[i] + b[i] + c[i]) for i in range(3)))
    require(min(areas) > 0 and min(outward) > 0, "Sphere triangles must have defined outward flat normals")
    inventory = []
    parents = {child: parent for parent, node in enumerate(document["nodes"]) for child in node.get("children", [])}
    def hierarchy_point(node, point):
        while True:
            value = document["nodes"][node]
            require("rotation" not in value and "matrix" not in value, "Unexpected rotated/matrix node")
            point = tuple(point[i] * value.get("scale", [1, 1, 1])[i] + value.get("translation", [0, 0, 0])[i] for i in range(3))
            if node not in parents:
                return point
            node = parents[node]
    for mesh, item in enumerate(document["meshes"]):
        require(len(item["primitives"]) == 1, "Primitive count changed")
        primitive = item["primitives"][0]
        mode = primitive.get("mode", 4)
        has_normal = "NORMAL" in primitive["attributes"]
        has_color = "COLOR_0" in primitive["attributes"]
        expected_mode = 4 if mesh == 24 else (0, 3, 4)[mesh % 3]
        expected_count = 4 if mesh == 24 else 205 if mesh % 3 == 2 else 65536
        require(mode == expected_mode and has_normal == (12 <= mesh < 24) and has_color == (mesh < 24 and mesh % 6 >= 3), "Authored primitive policy changed")
        count = document["accessors"][primitive["attributes"]["POSITION"]]["count"]
        node = 26 if mesh == 24 else mesh
        require(count == expected_count and document["nodes"][node]["mesh"] == mesh, "Vertex/instance count changed")
        for vertex in (0, count - 1):
            local = struct.unpack_from("<3f", blobs["Plane.bin"], vertex * 12) if mesh == 24 else vector_arrays[("Points.bin", "Lines.bin", "Triangles.bin")[mesh % 3]][vertex]
            literal = (local[0] * 20 - 10, local[1] * 14 - 7, local[2] - 1.5) if mesh == 24 else (local[0] + (mesh % 3 - 1) * 2.5 + (-4 if mesh < 12 else 4) + 2.5, local[1] + 3 - 3 * ((mesh % 12) // 3), local[2] + .5)
            require(all(abs(a - b) < 1e-12 for a, b in zip(literal, hierarchy_point(node, local))), "Literal oracle parent/scale formula is wrong")
        raw_index_count = document["accessors"][primitive["indices"]]["count"] if "indices" in primitive else count
        expanded = (count - 1) * 2 if mode == 3 else raw_index_count
        flat = mode == 4 and not has_normal
        inventory.append({"node": node, "mesh": mesh, "primitive": 0, "source_mode": mode, "draw_mode": 1 if mode == 3 else mode, "source_vertices": count, "output_vertices": expanded if flat else count, "expanded_indices": expanded, "normal_policy": "authored" if has_normal else "generated_flat" if flat else "direction_unspecified_recommended_unlit", "color": has_color, "material": primitive.get("material")})
    return {
        "corpus_revision": revision, "upstream_variants": variants, "license": "CC0-1.0; see upstream LICENSE.md",
        "files": [{"path": name, "bytes": len(data), "sha256": HASHES[name]} for name, data in blobs.items()],
        "primitive_instances": inventory, "primitive_instance_count": len(inventory),
        "total_source_vertices": sum(row["source_vertices"] for row in inventory),
        "total_output_vertices": sum(row["output_vertices"] for row in inventory),
        "total_expanded_indices": sum(row["expanded_indices"] for row in inventory),
        "sphere_triangle_count": len(indices) // 3, "sphere_minimum_cross_length_squared": min(areas),
        "authored_normal_length_ranges": normal_lengths,
        "scope": "independent Python pinned-source/geometry audit only; C# reader, compiled packaging, GPU and image gates unrun",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--checkout", type=Path, default=Path(__file__).resolve().parents[6])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = audit(args.checkout.resolve())
    text = json.dumps(result, indent=2) + "\n"
    if args.output:
        args.output.write_text(text)
    else:
        print(text, end="")
