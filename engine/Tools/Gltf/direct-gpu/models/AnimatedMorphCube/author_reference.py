#!/usr/bin/env python3
"""Independent source-only AnimatedMorphCube oracle. Never imports renderer code.

Writes source-described streams, authored LINEAR weights, world positions/normals
and a 60 Hz capture schedule. The schedule describes real samples to capture;
it neither renders nor synthesizes intermediate images.
"""
import argparse
import base64
from fractions import Fraction
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess

PIN = "90d7ede14c7e280af263824604b427a1ca02cb66"
COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
FORMATS = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}


def identity():
    return [[float(r == c) for c in range(4)] for r in range(4)]


def multiply(a, b):
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def local(node):
    if "matrix" in node:
        return [[node["matrix"][c * 4 + r] for c in range(4)] for r in range(4)]
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    length = math.sqrt(x*x + y*y + z*z + w*w)
    x, y, z, w = [v / length for v in (x, y, z, w)]
    rotation = [
        [1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w), 0],
        [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w), 0],
        [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y), 0],
        [0, 0, 0, 1],
    ]
    scale = identity()
    for i, value in enumerate(node.get("scale", [1, 1, 1])):
        scale[i][i] = value
    translation = identity()
    for i, value in enumerate(node.get("translation", [0, 0, 0])):
        translation[i][3] = value
    return multiply(translation, multiply(rotation, scale))


def transform(matrix, value, point):
    vector = [*value, 1 if point else 0]
    return [sum(matrix[r][k] * vector[k] for k in range(4)) for r in range(3)]


def normal_matrix(matrix):
    a, b, c = matrix[0][:3]
    d, e, f = matrix[1][:3]
    g, h, i = matrix[2][:3]
    cofactors = [[e*i-f*h, f*g-d*i, d*h-e*g],
                 [c*h-b*i, a*i-c*g, b*g-a*h],
                 [b*f-c*e, c*d-a*f, a*e-b*d]]
    determinant = a*cofactors[0][0] + b*cofactors[0][1] + c*cofactors[0][2]
    if determinant == 0:
        raise ValueError("Singular source normal matrix")
    output = identity()
    for r in range(3):
        for col in range(3):
            output[r][col] = cofactors[r][col] / determinant
    return output


def normalize(vector):
    length = math.sqrt(sum(value*value for value in vector))
    if length == 0:
        raise ValueError("Degenerate normal at requested sample")
    return [value / length for value in vector]


def tangent_direction(tangent, normal, matrix, world_normal):
    """Return only a mathematically defined projected direction; invent no fallback.

    Source-parallel N/T makes cross(N,T) zero. A signed morph can also cancel T.
    These vertices still require finite renderer output, unit/orthogonal fallback
    and preserved W; they cannot supply an independent expected XYZ direction.
    Exact zero checks intentionally do not waive small-but-defined directions.
    """
    if all(value == 0 for value in tangent):
        return None, "zero_morphed_tangent"
    cross = [normal[1]*tangent[2]-normal[2]*tangent[1],
             normal[2]*tangent[0]-normal[0]*tangent[2],
             normal[0]*tangent[1]-normal[1]*tangent[0]]
    if all(value == 0 for value in cross):
        return None, "morphed_tangent_parallel_to_normal"
    world = transform(matrix, tangent, False)
    projection = sum(world[index]*world_normal[index] for index in range(3))
    projected = [world[index]-projection*world_normal[index] for index in range(3)]
    if all(value == 0 for value in projected):
        return None, "projected_world_tangent_zero"
    return normalize(projected), None


def basis(vector, position):
    factor = 1 / .0254 if position else 1
    return [vector[2]*factor, vector[0]*factor, vector[1]*factor]


class Source:
    def __init__(self, path):
        self.path = path
        raw = path.read_bytes()
        binary = None
        if raw[:4] == b"glTF":
            magic, version, length = struct.unpack_from("<III", raw)
            if version != 2 or length != len(raw):
                raise ValueError("Unexpected GLB header")
            offset = 12
            while offset < len(raw):
                chunk_length, chunk_type = struct.unpack_from("<II", raw, offset)
                value = raw[offset+8:offset+8+chunk_length]
                if chunk_type == 0x4E4F534A:
                    self.data = json.loads(value)
                elif chunk_type == 0x004E4942:
                    binary = value
                offset += 8 + chunk_length
        else:
            self.data = json.loads(raw)
        self.buffers = []
        for buffer in self.data.get("buffers", []):
            uri = buffer.get("uri")
            value = binary if uri is None else base64.b64decode(uri.split(",", 1)[1]) if uri.startswith("data:") else (path.parent / uri).read_bytes()
            if value is None or len(value) < buffer["byteLength"]:
                raise ValueError("Missing or truncated source buffer")
            self.buffers.append(value)
        self.parents = {}
        for parent, node in enumerate(self.data["nodes"]):
            for child in node.get("children", []):
                if child in self.parents:
                    raise ValueError("Multiple parents")
                self.parents[child] = parent

    def world(self, node):
        result = local(self.data["nodes"][node])
        visited = {node}
        while node in self.parents:
            node = self.parents[node]
            if node in visited:
                raise ValueError("Node cycle")
            visited.add(node)
            result = multiply(local(self.data["nodes"][node]), result)
        return result

    def accessor(self, index):
        accessor = self.data["accessors"][index]
        count = COMPONENTS[accessor["type"]]
        fmt = FORMATS[accessor["componentType"]]
        size = struct.calcsize(fmt)
        output = [[0]*count for _ in range(accessor["count"])]
        if "bufferView" in accessor:
            view = self.data["bufferViews"][accessor["bufferView"]]
            stride = view.get("byteStride", count*size)
            start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
            for vertex in range(accessor["count"]):
                output[vertex] = list(struct.unpack_from("<" + fmt*count, self.buffers[view["buffer"]], start + vertex*stride))
        if "sparse" in accessor:
            sparse = accessor["sparse"]
            indices = sparse["indices"]
            index_view = self.data["bufferViews"][indices["bufferView"]]
            values = sparse["values"]
            value_view = self.data["bufferViews"][values["bufferView"]]
            index_fmt = FORMATS[indices["componentType"]]
            for replacement in range(sparse["count"]):
                target = struct.unpack_from("<"+index_fmt, self.buffers[index_view["buffer"]], index_view.get("byteOffset", 0)+indices.get("byteOffset", 0)+replacement*struct.calcsize(index_fmt))[0]
                output[target] = list(struct.unpack_from("<"+fmt*count, self.buffers[value_view["buffer"]], value_view.get("byteOffset", 0)+values.get("byteOffset", 0)+replacement*count*size))
        if accessor.get("normalized", False):
            divisor = {5120: 127, 5121: 255, 5122: 32767, 5123: 65535}[accessor["componentType"]]
            output = [[max(-1, value/divisor) if accessor["componentType"] in (5120, 5122) else value/divisor for value in vertex] for vertex in output]
        return output

    def describe(self, index):
        accessor = self.data["accessors"][index]
        view = self.data["bufferViews"][accessor["bufferView"]]
        start = view.get("byteOffset", 0)
        view_bytes = self.buffers[view["buffer"]][start:start+view["byteLength"]]
        return {"accessorIndex": index, "components": COMPONENTS[accessor["type"]],
                "count": accessor["count"], "componentType": accessor["componentType"],
                "normalized": accessor.get("normalized", False), "byteOffset": accessor.get("byteOffset", 0),
                "byteStride": view.get("byteStride", COMPONENTS[accessor["type"]]*struct.calcsize(FORMATS[accessor["componentType"]])),
                "viewByteOffset": start, "viewByteLength": view["byteLength"],
                "viewSha256": hashlib.sha256(view_bytes).hexdigest(), "values": self.accessor(index)}


def author(path, sample_root):
    source = Source(path)
    data = source.data
    animation = data["animations"][0]
    channel = animation["channels"][0]
    if len(animation["channels"]) != 1 or channel["target"]["path"] != "weights":
        raise ValueError("This oracle expects this model's one weight channel")
    node_index = channel["target"]["node"]
    mesh_index = data["nodes"][node_index]["mesh"]
    primitive = data["meshes"][mesh_index]["primitives"][0]
    sampler = animation["samplers"][channel["sampler"]]
    if sampler.get("interpolation", "LINEAR") != "LINEAR":
        raise ValueError("Unexpected interpolation")
    times = [element[0] for element in source.accessor(sampler["input"])]
    weights = [element[0] for element in source.accessor(sampler["output"])]
    target_count = len(primitive["targets"])
    if len(weights) != len(times)*target_count:
        raise ValueError("Weight shape mismatch")
    position = source.accessor(primitive["attributes"]["POSITION"])
    normals = source.accessor(primitive["attributes"]["NORMAL"])
    tangents = source.accessor(primitive["attributes"]["TANGENT"]) if "TANGENT" in primitive["attributes"] else None
    deltas = [{semantic: source.accessor(index) for semantic, index in target.items()} for target in primitive["targets"]]
    matrix = source.world(node_index)
    normals_matrix = normal_matrix(matrix)
    determinant = (matrix[0][0]*(matrix[1][1]*matrix[2][2]-matrix[1][2]*matrix[2][1])
                   - matrix[0][1]*(matrix[1][0]*matrix[2][2]-matrix[1][2]*matrix[2][0])
                   + matrix[0][2]*(matrix[1][0]*matrix[2][1]-matrix[1][1]*matrix[2][0]))
    source_handedness = -1 if determinant < 0 else 1

    def evaluate(time, overrides=None):
        requested_time = time
        time = struct.unpack("<f", struct.pack("<f", time))[0]
        interval = 0
        while interval+1 < len(times) and times[interval+1] <= time:
            interval += 1
        next_key = min(interval+1, len(times)-1)
        blend = 0 if interval == next_key else min(1, max(0, (time-times[interval])/(times[next_key]-times[interval])))
        sampled = [weights[interval*target_count+t]*(1-blend) + weights[next_key*target_count+t]*blend for t in range(target_count)]
        for target, value in (overrides or {}).items():
            if not isinstance(target, int) or not 0 <= target < target_count or not math.isfinite(value):
                raise ValueError("Invalid explicit manual morph override")
            sampled[target] = value
        positions, normal_values, tangent_values, tangent_signs, undefined_tangents = [], [], [], [], []
        for vertex in range(len(position)):
            p = list(position[vertex]); n = list(normals[vertex])
            t = list(tangents[vertex][:3]) if tangents is not None else None
            for target, weight in enumerate(sampled):
                for component in range(3):
                    p[component] += weight*deltas[target].get("POSITION", [[0]*3]*len(position))[vertex][component]
                    n[component] += weight*deltas[target].get("NORMAL", [[0]*3]*len(position))[vertex][component]
                    if t is not None:
                        t[component] += weight*deltas[target].get("TANGENT", [[0]*3]*len(position))[vertex][component]
            positions.append(basis(transform(matrix, p, True), True))
            world_normal = normalize(transform(normals_matrix, n, False))
            normal_values.append(basis(world_normal, False))
            if t is not None:
                world_tangent, reason = tangent_direction(t, n, matrix, world_normal)
                sign = tangents[vertex][3]*source_handedness
                tangent_signs.append(sign)
                if world_tangent is None:
                    tangent_values.append(None)
                    undefined_tangents.append({"vertex": vertex, "reason": reason})
                else:
                    tangent_values.append([*basis(world_tangent, False), sign])
        return {"time": time, "requestedTime": requested_time, "weights": sampled, "positionsInches": positions,
                "normals": normal_values, "tangents": tangent_values if tangents is not None else None,
                "tangentHandedness": tangent_signs if tangents is not None else None,
                "undefinedTangentDirections": undefined_tangents,
                "nodeHandedness": source_handedness}

    duration = times[-1]
    frame_count = math.ceil(duration*60)
    frame_times = [i/60 for i in range(frame_count)]
    delays = []
    for i, time in enumerate(frame_times):
        end = frame_times[i+1] if i+1 < frame_count else duration
        delay = Fraction(end-time).limit_denominator(65535)
        if delay.numerator < 1 or delay.numerator > 65535:
            raise ValueError("APNG duration cannot be represented in bounded rational fields")
        delays.append([delay.numerator, delay.denominator])
    check_times = sorted(set([*times, *(times[i]+(times[i+1]-times[i])*.5 for i in range(len(times)-1))]))
    if target_count != 2 or len(times) != 127:
        raise ValueError("Audit explicit manual cases if the pinned cube's target/key inventory changes")
    # These are independent raw-source geometry expectations, not rendered
    # frames. Partial overrides retain the current authored other weight.
    manual_cases = [("signed_first_"+label, 0, {0: value, 1: 0})
                    for label, value in (("minus2", -2), ("minus1", -1), ("zero", 0), ("one", 1), ("two", 2))]
    manual_cases += [("override_first_at_middle", 63, {0: -.5}),
                     ("override_first_after_seek", 32, {0: -.5}),
                     ("both_overridden", 63, {0: -.5, 1: 2.5}),
                     ("clear_first_keep_second", 63, {1: 2.5})]
    manual_samples = []
    for label, key_index, overrides in manual_cases:
        sample = evaluate(times[key_index], overrides)
        sample.update(label=label, overrides={str(target): value for target, value in overrides.items()})
        manual_samples.append(sample)
    return {"source": str(path.relative_to(sample_root)), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "node": node_index, "mesh": mesh_index, "primitive": 0, "vertexCount": len(position),
            "sourceIndices": [element[0] for element in source.accessor(primitive["indices"])],
            "bufferSha256": [hashlib.sha256(buffer).hexdigest() for buffer in source.buffers],
            "targetStreams": [{semantic: source.describe(index) for semantic, index in target.items()} for target in primitive["targets"]],
            "keyTimes": times, "duration": duration, "targetCount": target_count,
            "samples": [evaluate(time) for time in check_times],
            "manualSamples": manual_samples,
            "media": {"nominalFramesPerSecond": 60, "times": frame_times, "delays": delays,
                      "encodedDuration": sum(n/d for n, d in delays), "endpoint": evaluate(duration),
                      "samples": [evaluate(time) for time in frame_times]}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sample-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = args.sample_root.resolve()
    actual_pin = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"], check=True, capture_output=True, text=True).stdout.strip()
    if actual_pin != PIN:
        raise ValueError(f"Expected pinned sample revision {PIN}, found {actual_pin}")
    forms = [("glTF", "gltf"), ("glTF-Binary", "glb"), ("glTF-Quantized", "gltf")]
    output = {"model": "AnimatedMorphCube", "pin": PIN, "kind": "independent-source-oracle-not-GPU-evidence",
              "forms": [author(root/"Models"/"AnimatedMorphCube"/folder/("AnimatedMorphCube."+extension), root) for folder, extension in forms]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, separators=(",", ":")) + "\n")
    print(json.dumps({"output": str(args.output), "forms": len(output["forms"]),
                      "checks": [len(form["samples"]) for form in output["forms"]],
                      "mediaFrames": [len(form["media"]["times"]) for form in output["forms"]],
                      "sha256": hashlib.sha256(args.output.read_bytes()).hexdigest()}))


if __name__ == "__main__":
    main()
