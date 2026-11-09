"""Small independent oracle for the pinned Khronos OrientationTest asset.

This is test-only reference math, not an alternate engine importer or renderer.
It never imports Sandbox or consumes production coordinate/pose helpers. The
coordinator generates expected JSON before loading the sample in s&box, then
compares GPU readback and native images against these expectations.
"""

import argparse
import base64
import hashlib
import json
import math
from pathlib import Path
import struct


HERE = Path(__file__).resolve().parent
CHECKOUT = HERE.parents[5]
SAMPLE = CHECKOUT / "Temp/gltf-reference/glTF-Sample-Assets/Models/OrientationTest"
INCHES_PER_METRE = 1 / .0254
IDENTITY = (1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)
COMPONENTS = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
ARITY = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def geometry_fingerprint(value):
    # JSON 0 and 0.0 (and signed zero) represent the same glTF number. Source
    # hashes remain byte-exact; this fingerprint compares decoded geometry and
    # material values without rounding or tolerating nonzero numeric differences.
    def canonical(item):
        if isinstance(item, bool) or item is None or isinstance(item, str):
            return item
        if isinstance(item, (int, float)):
            number = float(item)
            if not math.isfinite(number):
                raise ValueError("Nonfinite reference geometry")
            return 0.0 if number == 0 else number
        if isinstance(item, dict):
            return {key: canonical(child) for key, child in item.items()}
        return [canonical(child) for child in item]
    return sha256(json.dumps(canonical(value), sort_keys=True, separators=(",", ":")).encode())


def engine_position(value):
    x, y, z = value
    return (z * INCHES_PER_METRE, x * INCHES_PER_METRE, y * INCHES_PER_METRE)


def engine_direction(value):
    x, y, z = value
    return (z, x, y)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def normalize(value):
    length = math.sqrt(sum(v * v for v in value))
    if length == 0:
        raise ValueError("Reference normal is zero")
    return tuple(v / length for v in value)


def determinant(matrix):
    return sum(a * b for a, b in zip(matrix[:3], cross(matrix[4:7], matrix[8:11])))


def multiply(parent, local):
    # glTF stores columns consecutively and applies column vectors.
    return tuple(sum(parent[k * 4 + row] * local[col * 4 + k] for k in range(4)) for col in range(4) for row in range(4))


def transform_point(matrix, point):
    return tuple(sum(matrix[col * 4 + row] * point[col] for col in range(3)) + matrix[12 + row] for row in range(3))


def transform_normal(matrix, normal):
    cofactors = (cross(matrix[4:7], matrix[8:11]), cross(matrix[8:11], matrix[:3]), cross(matrix[:3], matrix[4:7]))
    det = determinant(matrix)
    if det == 0:
        raise ValueError("Singular orientation fixture does not define an inverse-transpose normal")
    return normalize(tuple(sum(cofactors[col][row] * normal[col] for col in range(3)) / det for row in range(3)))


def transpose_linear(matrix):
    result = list(matrix)
    for col in range(3):
        for row in range(3):
            result[col * 4 + row] = matrix[row * 4 + col]
    return tuple(result)


def node_matrix(node, transpose_quaternions=False, transpose_matrices=False):
    if "matrix" in node:
        matrix = tuple(node["matrix"])
        return transpose_linear(matrix) if transpose_matrices else matrix
    x, y, z, w = node.get("rotation", (0, 0, 0, 1))
    matrix = (
        1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y), 0,
        2 * (x * y - w * z), 1 - 2 * (x * x + z * z), 2 * (y * z + w * x), 0,
        2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y), 0,
        0, 0, 0, 1,
    )
    if transpose_quaternions:
        matrix = transpose_linear(matrix)
    result = list(matrix)
    for col, scale in enumerate(node.get("scale", (1, 1, 1))):
        for row in range(3):
            result[col * 4 + row] *= scale
    result[12:15] = node.get("translation", (0, 0, 0))
    return tuple(result)


def sample_variants():
    return {
        "glTF": SAMPLE / "glTF/OrientationTest.gltf",
        "glTF-Embedded": SAMPLE / "glTF-Embedded/OrientationTest.gltf",
        "glTF-Binary": SAMPLE / "glTF-Binary/OrientationTest.glb",
    }


def read_container(path):
    path = Path(path).resolve()
    data = path.read_bytes()
    binary = None
    if data[:4] == b"glTF":
        magic, version, length = struct.unpack_from("<III", data)
        if magic != 0x46546C67 or version != 2 or length != len(data):
            raise ValueError("Bad reference GLB header")
        offset = 12
        chunks = []
        while offset < len(data):
            length, kind = struct.unpack_from("<II", data, offset)
            offset += 8
            if length % 4 or offset + length > len(data):
                raise ValueError("Bad reference GLB chunk")
            chunks.append((kind, data[offset:offset + length]))
            offset += length
        if not chunks or chunks[0][0] != 0x4E4F534A:
            raise ValueError("No JSON-first reference GLB")
        root = json.loads(chunks[0][1])
        binary_chunks = [value for kind, value in chunks if kind == 0x004E4942]
        if len(binary_chunks) > 1:
            raise ValueError("Duplicate reference BIN")
        binary = binary_chunks[0] if binary_chunks else None
    else:
        root = json.loads(data)
    buffers = []
    for index, descriptor in enumerate(root.get("buffers", [])):
        uri = descriptor.get("uri")
        if uri is None:
            if index != 0 or binary is None:
                raise ValueError("Reference buffer has no data")
            content = binary
        elif uri.startswith("data:"):
            header, payload = uri.split(",", 1)
            if not header.endswith(";base64"):
                raise ValueError("Reference oracle accepts only base64 data URIs")
            content = base64.b64decode(payload, validate=True)
        else:
            resolved = (path.parent / uri).resolve()
            if not resolved.is_relative_to(path.parent):
                raise ValueError("Reference dependency escapes its model directory")
            content = resolved.read_bytes()
        declared = descriptor["byteLength"]
        if len(content) < declared:
            raise ValueError("Truncated reference buffer")
        buffers.append(content[:declared])
    return root, buffers


def read_accessor(root, buffers, index):
    accessor = root["accessors"][index]
    code, size = COMPONENTS[accessor["componentType"]]
    arity = ARITY[accessor["type"]]
    count = accessor["count"]
    result = [tuple(0 for _ in range(arity)) for _ in range(count)]

    def values(view_index, offset, number, stride=None):
        view = root["bufferViews"][view_index]
        source = buffers[view["buffer"]]
        begin = view.get("byteOffset", 0)
        stride = view.get("byteStride", arity * size) if stride is None else stride
        if offset < 0 or offset + max(0, number - 1) * stride + arity * size > view["byteLength"]:
            raise ValueError("Reference accessor exceeds its view")
        return [struct.unpack_from("<" + code * arity, source, begin + offset + i * stride) for i in range(number)]

    if "bufferView" in accessor:
        result = values(accessor["bufferView"], accessor.get("byteOffset", 0), count)
    sparse = accessor.get("sparse")
    if sparse:
        indices = sparse["indices"]
        view = root["bufferViews"][indices["bufferView"]]
        index_code, index_size = COMPONENTS[indices["componentType"]]
        start = view.get("byteOffset", 0) + indices.get("byteOffset", 0)
        targets = [struct.unpack_from("<" + index_code, buffers[view["buffer"]], start + i * index_size)[0] for i in range(sparse["count"])]
        replacements = values(sparse["values"]["bufferView"], sparse["values"].get("byteOffset", 0), sparse["count"], arity * size)
        if targets != sorted(set(targets)) or any(target >= count for target in targets):
            raise ValueError("Invalid sparse reference indices")
        for target, replacement in zip(targets, replacements):
            result[target] = replacement
    if accessor.get("normalized"):
        component_type = accessor["componentType"]
        def normalized(value):
            if component_type == 5120:
                return max(-1, value / 127)
            if component_type == 5121:
                return value / 255
            if component_type == 5122:
                return max(-1, value / 32767)
            if component_type == 5123:
                return value / 65535
            raise ValueError("Unsupported normalized reference component")
        result = [tuple(normalized(component) for component in value) for value in result]
    return result


def describe(root, buffers, transpose_quaternions=False, transpose_matrices=False):
    primitives = []
    nodes = root.get("nodes", [])
    visited = set()

    def visit(index, parent):
        if index in visited:
            raise ValueError("Pinned default scene is not a tree")
        visited.add(index)
        node = nodes[index]
        world = multiply(parent, node_matrix(node, transpose_quaternions, transpose_matrices))
        if "mesh" in node:
            mesh_index = node["mesh"]
            for ordinal, primitive in enumerate(root["meshes"][mesh_index]["primitives"]):
                if primitive.get("mode", 4) != 4:
                    raise ValueError("OrientationTest oracle currently expects triangles")
                attributes = primitive["attributes"]
                positions = read_accessor(root, buffers, attributes["POSITION"])
                source_world = [transform_point(world, point) for point in positions]
                normals = read_accessor(root, buffers, attributes["NORMAL"])
                index_accessor = primitive.get("indices")
                indices = list(range(len(positions))) if index_accessor is None else [value[0] for value in read_accessor(root, buffers, index_accessor)]
                primitives.append({
                    "node": index, "node_name": node.get("name"), "mesh": mesh_index, "primitive": ordinal,
                    "material": primitive.get("material"), "source_world_matrix": world,
                    "source_positions": positions, "source_normals": normals,
                    "source_world_positions": source_world,
                    "engine_world_positions": [engine_position(point) for point in source_world],
                    "engine_world_normals": [engine_direction(transform_normal(world, normal)) for normal in normals],
                    "indices": indices,
                    "negative_determinant": determinant(world) < 0,
                    "index_component_type": None if index_accessor is None else root["accessors"][index_accessor]["componentType"],
                })
        for child in node.get("children", []):
            visit(child, world)

    for index in root["scenes"][root["scene"]].get("nodes", []):
        visit(index, IDENTITY)
    materials = root.get("materials", [])
    geometry = {"primitives": sorted(primitives, key=lambda item: (item["node"], item["primitive"])), "materials": materials}
    return {
        **geometry,
        "geometry_sha256": geometry_fingerprint(geometry),
        "animation_count": len(root.get("animations", [])), "skin_count": len(root.get("skins", [])),
    }


def describe_sample(path):
    root, buffers = read_container(path)
    return {**describe(root, buffers), "source": str(Path(path).resolve()), "source_sha256": sha256(Path(path).read_bytes())}


def closest_distance(first, second):
    return min(math.dist(a, b) for a in first for b in second)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    records = {variant: describe_sample(path) for variant, path in sample_variants().items()}
    output = {
        "scope": "Independent reference positions/normals/indices and source transforms only; no engine or GPU proof",
        "sample": "OrientationTest", "oracle_sha256": sha256(Path(__file__).read_bytes()),
        "camera_source_directions": [[1, .65, 1], [-1, .65, 1]],
        "camera_engine_directions": [[1, 1, .65], [1, -1, .65]],
        "source_up": [0, 1, 0], "engine_up": [0, 0, 1], "variants": records,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(output, indent=2) + "\n")
    print(json.dumps({"output": str(args.out), "sha256": sha256(args.out.read_bytes()), "variants": len(records), "geometry_sha256": {key: value["geometry_sha256"] for key, value in records.items()}}))


if __name__ == "__main__":
    main()
