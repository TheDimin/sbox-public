#!/usr/bin/env python3
"""Author independent CesiumMan test input, never an engine/rendering test result.

Only Python's stdlib reads source containers/accessors and evaluates this model's
LINEAR channels. Sandbox.Gltf, its pose sampler, and backend code are not imported.
The scoped reader intentionally rejects unsupported codecs/sparse/matrix padding;
it never substitutes zero data for a format it cannot independently decode.
"""
import argparse
import base64
import bisect
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
from urllib.parse import unquote, urlsplit

PIN = "90d7ede14c7e280af263824604b427a1ca02cb66"
VARIANTS = ("glTF", "glTF-Embedded", "glTF-Binary", "glTF-Draco")
COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
FORMATS = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def identity():
    return [[float(i == j) for j in range(4)] for i in range(4)]


def multiply(left, right):
    return [[sum(left[i][k] * right[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def column_major(values):
    return [[values[j * 4 + i] for j in range(4)] for i in range(4)]


def row_transform(matrix):
    # A column-vector glTF matrix transposes to the engine's row-vector convention.
    return [matrix[j][i] for i in range(4) for j in range(4)]


def normalized(vector):
    length = math.sqrt(sum(value * value for value in vector))
    require(length > 1e-20 and math.isfinite(length), "Zero/nonfinite independent reference vector")
    return [value / length for value in vector]


def trs(translation, quaternion, scale):
    x, y, z, w = normalized(quaternion)
    sx, sy, sz = scale
    tx, ty, tz = translation
    return [[(1 - 2 * (y*y + z*z))*sx, 2*(x*y-z*w)*sy, 2*(x*z+y*w)*sz, tx],
            [2*(x*y+z*w)*sx, (1-2*(x*x+z*z))*sy, 2*(y*z-x*w)*sz, ty],
            [2*(x*z-y*w)*sx, 2*(y*z+x*w)*sy, (1-2*(x*x+y*y))*sz, tz],
            [0.0, 0.0, 0.0, 1.0]]


def slerp(left, right, ratio):
    left, right = normalized(left), normalized(right)
    dot = sum(a*b for a, b in zip(left, right))
    if dot < 0:
        right, dot = [-value for value in right], -dot
    dot = min(1.0, max(-1.0, dot))
    if dot > .9995:
        return normalized([a + ratio*(b-a) for a, b in zip(left, right)])
    angle = math.acos(dot)
    first, second = math.sin((1-ratio)*angle) / math.sin(angle), math.sin(ratio*angle) / math.sin(angle)
    return normalized([a*first+b*second for a, b in zip(left, right)])


class Source:
    def __init__(self, corpus, path):
        self.corpus, self.path = corpus, path
        self.dependencies, self.cache = {}, {}
        raw = path.read_bytes()
        binary = None
        if raw[:4] == b"glTF":
            magic, version, length = struct.unpack_from("<4sII", raw)
            require(magic == b"glTF" and version == 2 and length == len(raw), "Invalid reference GLB")
            position, chunks = 12, []
            while position < len(raw):
                size, kind = struct.unpack_from("<II", raw, position)
                require(size % 4 == 0 and position+8+size <= len(raw), "Invalid GLB reference chunk")
                chunks.append((kind, raw[position+8:position+8+size]))
                position += 8+size
            require(chunks and chunks[0][0] == 0x4E4F534A, "GLB reference has no first JSON chunk")
            self.doc = json.loads(chunks[0][1])
            binary = next((value for kind, value in chunks if kind == 0x004E4942), None)
        else:
            self.doc = json.loads(raw)
        self.buffers = []
        for index, info in enumerate(self.doc.get("buffers", [])):
            uri = info.get("uri")
            if uri is None:
                require(index == 0 and binary is not None, "Missing independent reference buffer")
                data = binary
            elif uri.startswith("data:"):
                header, content = uri.split(",", 1)
                require(header.endswith(";base64"), "Reference reader needs base64 data URI")
                data = base64.b64decode(content, validate=True)
            else:
                dependency = self.external(uri)
                data = dependency.read_bytes()
                self.dependencies[str(dependency.relative_to(corpus))] = digest(data)
            require(len(data) >= info["byteLength"], "Reference buffer is shorter than declared")
            self.buffers.append(data[:info["byteLength"]])
        # Include the independently managed texture as a provenance dependency,
        # without using image decoding to pretend this is appearance validation.
        for image in self.doc.get("images", []):
            uri = image.get("uri")
            if uri and not uri.startswith("data:"):
                dependency = self.external(uri)
                self.dependencies[str(dependency.relative_to(corpus))] = digest(dependency.read_bytes())

    def external(self, uri):
        parts = urlsplit(uri)
        require(not(parts.scheme or parts.netloc or parts.query or parts.fragment), "Nonlocal reference URI")
        target = (self.path.parent / unquote(parts.path)).resolve(strict=True)
        require(target.is_file() and target.is_relative_to(self.corpus), "Reference URI escapes pinned corpus")
        return target

    def accessor(self, index):
        if index in self.cache:
            return self.cache[index]
        info = self.doc["accessors"][index]
        require("sparse" not in info and "bufferView" in info, "Independent Cesium reader cannot decode sparse/codec accessors")
        view = self.doc["bufferViews"][info["bufferView"]]
        require("extensions" not in view, "Independent Cesium reader cannot decode compressed views")
        components = COMPONENTS[info["type"]]
        code = FORMATS[info["componentType"]]
        size = struct.calcsize("<"+code)
        element = size*components
        stride = view.get("byteStride", element)
        offset = info.get("byteOffset", 0)
        length = offset + (info["count"]-1)*stride + element
        require(stride >= element and length <= view["byteLength"], "Independent accessor exceeds source view")
        start = view.get("byteOffset", 0)
        data = self.buffers[view["buffer"]]
        require(start + view["byteLength"] <= len(data), "Independent bufferView exceeds source buffer")
        tight = b"".join(data[start+offset+i*stride:start+offset+i*stride+element] for i in range(info["count"]))
        values = [list(struct.unpack_from("<"+code*components, tight, i*element)) for i in range(info["count"])]
        if info.get("normalized", False):
            denominators = {5120: 127, 5121: 255, 5122: 32767, 5123: 65535}
            denominator = denominators[info["componentType"]]
            values = [[max(-1.0, value/denominator) if code in ("b", "h") else value/denominator for value in row] for row in values]
        result = {"values": values, "raw": tight, "offset": offset, "stride": stride,
                  "component_type": info["componentType"], "components": components,
                  "count": info["count"], "normalized": info.get("normalized", False)}
        self.cache[index] = result
        return result

    def worlds(self, time):
        updates = {}
        if time is not None:
            animation = self.doc["animations"][0]
            for channel in animation["channels"]:
                sampler = animation["samplers"][channel["sampler"]]
                require(sampler.get("interpolation", "LINEAR") == "LINEAR", "Cesium reference only claims LINEAR evaluation")
                times = [row[0] for row in self.accessor(sampler["input"])["values"]]
                values = self.accessor(sampler["output"])["values"]
                target = channel["target"]
                if time <= times[0]:
                    result = values[0]
                elif time >= times[-1]:
                    result = values[-1]
                else:
                    right = bisect.bisect_right(times, time)
                    left = right-1
                    ratio = (time-times[left]) / (times[right]-times[left])
                    result = slerp(values[left], values[right], ratio) if target["path"] == "rotation" else [a+(b-a)*ratio for a, b in zip(values[left], values[right])]
                updates[(target["node"], target["path"])] = result
        nodes = self.doc["nodes"]
        parents = [-1]*len(nodes)
        for index, node in enumerate(nodes):
            for child in node.get("children", []):
                require(parents[child] == -1, "Repeated reference parent")
                parents[child] = index
        locals_ = []
        for index, node in enumerate(nodes):
            if "matrix" in node:
                require(not any((index, path) in updates for path in ("translation", "rotation", "scale")), "TRS animation targets matrix node")
                matrix = column_major(node["matrix"])
            else:
                matrix = trs(updates.get((index, "translation"), node.get("translation", [0, 0, 0])),
                             updates.get((index, "rotation"), node.get("rotation", [0, 0, 0, 1])),
                             updates.get((index, "scale"), node.get("scale", [1, 1, 1])))
            locals_.append(matrix)
        worlds = []
        for index in range(len(nodes)):
            matrix, current, seen = identity(), index, set()
            while current != -1:
                require(current not in seen, "Cyclic reference hierarchy")
                seen.add(current)
                matrix = multiply(locals_[current], matrix)
                current = parents[current]
            worlds.append(matrix)
        return worlds


def transform_normal(matrix, normal):
    # Explicit cofactor inverse-transpose, separate from any engine matrix API.
    a, b, c = matrix[0][:3]
    d, e, f = matrix[1][:3]
    g, h, i = matrix[2][:3]
    cofactors = [[e*i-f*h, f*g-d*i, d*h-e*g],
                 [c*h-b*i, a*i-c*g, b*g-a*h],
                 [b*f-c*e, c*d-a*f, a*e-b*d]]
    determinant = a*cofactors[0][0] + b*cofactors[0][1] + c*cofactors[0][2]
    require(abs(determinant) > 1e-15, "Singular independent skin normal transform")
    return normalized([sum(cofactors[row][col]*normal[col] for col in range(3))/determinant for row in range(3)])


def sample(source, label, time):
    doc = source.doc
    primitive = doc["meshes"][0]["primitives"][0]
    attributes = {name: source.accessor(index)["values"] for name, index in primitive["attributes"].items()}
    skin = doc["skins"][0]
    worlds = source.worlds(time)
    inverse_binds = [column_major(row) for row in source.accessor(skin["inverseBindMatrices"])["values"]]
    palette = [multiply(worlds[node], inverse_binds[index]) for index, node in enumerate(skin["joints"])]
    positions, normals = [], []
    sets = sorted(int(name.split("_")[1]) for name in attributes if name.startswith("JOINTS_"))
    for vertex, point in enumerate(attributes["POSITION"]):
        blend = [[0.0]*4 for _ in range(4)]
        for influence_set in sets:
            joints = attributes[f"JOINTS_{influence_set}"][vertex]
            weights = attributes[f"WEIGHTS_{influence_set}"][vertex]
            for joint, weight in zip(joints, weights):
                require(0 <= joint < len(palette) and weight >= 0 and math.isfinite(weight), "Invalid independent skin influence")
                if weight:
                    for row in range(4):
                        for col in range(4):
                            blend[row][col] += palette[joint][row][col]*weight
        positions.append([sum(blend[row][col]*point[col] for col in range(3))+blend[row][3] for row in range(3)])
        normals.append(transform_normal(blend, attributes["NORMAL"][vertex]))
    return {"label": label, "time": time, "world_row_transform": [row_transform(matrix) for matrix in worlds],
            "palette_row_transform_gltf": [row_transform(matrix) for matrix in palette],
            "positions_gltf": positions, "normals_gltf": normals}


def author(corpus, output):
    actual = subprocess.run(["git", "-C", str(corpus), "rev-parse", "HEAD"], check=True, capture_output=True, text=True).stdout.strip()
    require(actual == PIN, "Reference corpus is not pinned to the reviewed revision")
    model = corpus / "Models/CesiumMan"
    assets = {}
    canonical = None
    core_sources = {}
    for variant in VARIANTS:
        path = model / variant / ("CesiumMan.glb" if variant == "glTF-Binary" else "CesiumMan.gltf")
        source = Source(corpus, path)
        primitive = source.doc["meshes"][0]["primitives"][0]
        entry = {"relative": str(path.relative_to(corpus)), "sha256": digest(path.read_bytes()),
                 "dependencies": source.dependencies, "mesh_node": 2, "attributes": {},
                 "required_extensions": source.doc.get("extensionsRequired", [])}
        if variant != "glTF-Draco":
            core_sources[variant] = source
            for name, index in primitive["attributes"].items():
                accessor = source.accessor(index)
                entry["attributes"][name] = {key: value for key, value in accessor.items() if key not in ("raw", "values")}
                entry["attributes"][name]["raw_sha256"] = digest(accessor["raw"])
            entry["indices_raw_sha256"] = digest(source.accessor(primitive["indices"])["raw"])
            if canonical is None:
                canonical = source
            else:
                for name, index in primitive["attributes"].items():
                    require(entry["attributes"][name]["raw_sha256"] == assets["glTF"]["attributes"][name]["raw_sha256"], "Core variants no longer have identical geometry")
        assets[variant] = entry
    variants = {}
    for variant, source in core_sources.items():
        animation = source.doc["animations"][0]
        key_times = [row[0] for row in source.accessor(animation["samplers"][0]["input"])["values"]]
        for sampler in animation["samplers"]:
            require([row[0] for row in source.accessor(sampler["input"])["values"]] == key_times, "Cesium channels no longer share their reviewed time schedule within variant "+variant)
        times = [("authored_rest", None), ("before_nonzero_first_key", 0.0), ("first_key", key_times[0]),
                 ("between_first_two_keys", f32((key_times[0]+key_times[1])/2)), ("key_12", key_times[11]),
                 ("between_keys_12_13", f32((key_times[11]+key_times[12])/2)), ("key_24", key_times[23]),
                 ("key_30", key_times[29]), ("last_key", key_times[-1]), ("after_last_key", 2.25)]
        primitive = source.doc["meshes"][0]["primitives"][0]
        vertex_count = source.doc["accessors"][primitive["attributes"]["POSITION"]]["count"]
        references = [sample(source, label, time) for label, time in times]
        require(any(abs(a-b) > 1e-5 for p, q in zip(references[0]["positions_gltf"], references[1]["positions_gltf"]) for a, b in zip(p, q)), "Bind-vs-first-key negative control lost sensitivity")
        for reference in references:
            reference["variant"] = variant
        variants[variant] = {"vertex_count": vertex_count,
                             "indices": [row[0] for row in source.accessor(primitive["indices"])["values"]],
                             "key_times": key_times,
                             "checked_vertices": sorted(set([0, 1, 2, vertex_count-1, *range(0, vertex_count, 97)])),
                             "samples": references}
    payload = {"version": 2, "pin": PIN, "scope": "Independent test-input authoring only; no engine, runtime or GPU pass claim.",
               "coordinate_convention": "glTF meters, column-vector evaluation; matrices stored in transposed row-transform sequence",
               "assets": assets, "variants": variants, "author_sha256": digest(Path(__file__).read_bytes()),
               "limitations": ["Draco geometry has different vertex order/count and requires a separate independent codec/raster check; it is not compared by source vertex index.",
                               "Core variants have identical geometry but different key counts/end times; each variant has its own independently evaluated pose oracle.",
                               "Normal reference uses inverse-transpose of the unnormalized weighted skin affine matrix.",
                               "No image decoding, stock shader appearance, Model.Load backend or timing performance is proven by this file."]}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, separators=(",", ":"), allow_nan=False)+"\n", encoding="utf-8")
    return {"output": str(output), "sha256": digest(output.read_bytes()), "bytes": output.stat().st_size,
            "samples": sum(len(value["samples"]) for value in variants.values()), "core_variant_oracles": len(variants), "vertices_per_sample": vertex_count, "source_variants": len(assets),
            "scope": payload["scope"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    print(json.dumps(author(arguments.corpus.resolve(strict=True), arguments.output.resolve())))
