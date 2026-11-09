#!/usr/bin/env python3
"""Author pose test input from this Draco variant's separate-integration decode.

No core vertex mapping and no Sandbox.Gltf/Evergine evaluator is used. The scoped
TRS/skin math is shared only with our independent source-reference author.
"""
import argparse
import base64
import importlib.util
import json
from pathlib import Path
import struct

math_path = Path(__file__).with_name("author_reference.py")
spec = importlib.util.spec_from_file_location("cesium_source_reference", math_path)
reference_math = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reference_math)


def author(corpus, decoded_path, output):
    decoded_bytes = decoded_path.read_bytes()
    decoded = json.loads(decoded_bytes)
    r = reference_math
    r.require(decoded["version"] == 1 and decoded["pin"] == r.PIN, "Unexpected Draco decode input version/pin")
    source_path = (corpus / decoded["source"]["relative"]).resolve(strict=True)
    r.require(source_path.is_relative_to(corpus), "Decoded reference source escapes corpus")
    source = r.Source(corpus, source_path)
    r.require(r.digest(source_path.read_bytes()) == decoded["source"]["sha256"], "Draco source changed after separate decode")
    buffer_path = (corpus / decoded["source"]["buffer_relative"]).resolve(strict=True)
    r.require(buffer_path.is_relative_to(corpus) and r.digest(buffer_path.read_bytes()) == decoded["source"]["buffer_sha256"], "Draco source buffer changed after separate decode")
    primitive = source.doc["meshes"][0]["primitives"][0]
    for semantic, accessor_index in primitive["attributes"].items():
        accessor = source.doc["accessors"][accessor_index]
        data = decoded["attributes"][semantic]
        r.require(data["component_type"] == accessor["componentType"] and data["count"] == accessor["count"], "Actual decoded accessor format/count differs from source "+semantic)
        raw = base64.b64decode(data["raw_base64"], validate=True)
        r.require(r.digest(raw) == data["raw_sha256"], "Decoded reference bytes changed for "+semantic)
        components = r.COMPONENTS[accessor["type"]]
        code = r.FORMATS[accessor["componentType"]]
        element = struct.calcsize("<"+code*components)
        r.require(data["components"] == components and len(raw) == accessor["count"]*element, "Decoded reference component extent differs from source")
        values = [list(struct.unpack_from("<"+code*components, raw, vertex*element)) for vertex in range(accessor["count"])]
        r.require(not accessor.get("normalized", False), "Cesium Draco scoped pose author needs explicit normalized decoding before supporting a changed normalized source")
        source.cache[accessor_index] = {"values": values, "raw": raw, "count": accessor["count"], "components": components,
                                        "component_type": accessor["componentType"], "normalized": False, "offset": 0, "stride": element}
    r.require(decoded["weight_summary"]["negative_components"] == 0,
              "Actual Draco decoded source has negative weights; preserve evidence and review upstream asset before choosing a policy. No clamping reference will be authored.")
    animation = source.doc["animations"][0]
    keys = [row[0] for row in source.accessor(animation["samplers"][0]["input"])["values"]]
    times = [("authored_rest", None), ("before_nonzero_first_key", 0.0), ("first_key", keys[0]),
             ("between_first_two_keys", r.f32((keys[0]+keys[1])/2)), ("key_12", keys[11]),
             ("between_keys_12_13", r.f32((keys[11]+keys[12])/2)), ("key_24", keys[23]),
             ("key_30", keys[29]), ("last_key", keys[-1]), ("after_last_key", r.f32(keys[-1]+.25))]
    samples = [r.sample(source, label, time) for label, time in times]
    for sample in samples:
        sample["variant"] = "glTF-Draco"
    count = decoded["vertex_count"]
    result = {"version": 1, "pin": r.PIN, "variant": "glTF-Draco", "vertex_count": count,
              "source_sha256": decoded["source"]["sha256"], "decoded_reference_sha256": r.digest(decoded_bytes),
              "decoder": decoded["decoder"], "key_times": keys, "indices": decoded["indices"],
              "checked_vertices": sorted(set([0, 1, 2, count-1, *range(0, count, 97)])), "samples": samples,
              "author_sha256": r.digest(Path(__file__).read_bytes()), "independent_math_sha256": r.digest(math_path.read_bytes()),
              "scope": "Test-input authoring only. Variant-local separately integrated decoder output and independent TRS/skin reference; shared codec ancestry explicit. No engine/GPU pass claim."}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, separators=(",", ":"), allow_nan=False)+"\n", encoding="utf-8")
    return {"output": str(output), "sha256": r.digest(output.read_bytes()), "bytes": output.stat().st_size,
            "samples": len(samples), "vertex_count": count, "scope": result["scope"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--decoded", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(author(args.corpus.resolve(strict=True), args.decoded.resolve(strict=True), args.output.resolve())))
