#!/usr/bin/env python3
"""Author a pinned corpus inventory and bounded, independent static expectations.

This is metadata authoring, not engine execution or rendering validation. Only
stdlib readers are used; Sandbox.Gltf is never invoked. Large/unsupported oracle
inputs remain explicit coverage holes, never exclusions from valid glTF scope.
"""
import argparse
import base64
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
from urllib.parse import unquote, unquote_to_bytes, urlsplit

PIN = "90d7ede14c7e280af263824604b427a1ca02cb66"
MAX_SPAN = 2 * 1024 * 1024
MAX_ELEMENTS = 100000


def require(value, message):
    if not value:
        raise ValueError(message)


def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON property: {key}")
        result[key] = value
    return result


def load_json(raw):
    return json.loads(raw, object_pairs_hook=unique)


def sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(65536), b""):
            digest.update(block)
    return digest.hexdigest()


def git(root, *arguments):
    return subprocess.run(["git", "-C", str(root), *arguments], check=True,
                          capture_output=True, text=True).stdout.strip()


def safe(root, relative):
    path = (root / relative).resolve(strict=True)
    require(path.is_relative_to(root) and path.is_file(), f"Escaping/missing corpus file: {relative}")
    with path.open("rb") as stream:
        require(not stream.read(43).startswith(b"version https://git-lfs.github.com/spec/v1"),
                f"Unfetched LFS payload: {relative}")
    return path


def container(path):
    if path.suffix.lower() == ".gltf":
        return load_json(path.read_text(encoding="utf-8")), None
    with path.open("rb") as stream:
        magic, version, length = struct.unpack("<4sII", stream.read(12))
        require(magic == b"glTF" and version == 2 and length == path.stat().st_size, "Invalid GLB header")
        json_length, kind = struct.unpack("<II", stream.read(8))
        require(kind == 0x4E4F534A and json_length % 4 == 0 and json_length <= 32 * 1024 * 1024,
                "Invalid/oversized GLB JSON chunk")
        doc = load_json(stream.read(json_length).decode("utf-8"))
        binary = None
        while stream.tell() < length:
            size, kind = struct.unpack("<II", stream.read(8))
            require(size % 4 == 0 and stream.tell() + size <= length, "Invalid GLB chunk")
            if kind == 0x004E4942:
                require(binary is None, "Duplicate GLB BIN chunk")
                binary = (stream.tell(), size)
            stream.seek(size, 1)
        return doc, binary


def external(root, asset, uri):
    parts = urlsplit(uri)
    require(not (parts.scheme or parts.netloc or parts.query or parts.fragment), f"Unsupported external URI: {uri}")
    path = (asset.parent / unquote(parts.path)).resolve(strict=True)
    require(path.is_relative_to(root), f"External URI escapes corpus: {uri}")
    return str(path.relative_to(root))


def identity():
    return [[float(i == j) for j in range(4)] for i in range(4)]


def multiply(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def local(node):
    if "matrix" in node:
        values = node["matrix"]
        return [[values[j * 4 + i] for j in range(4)] for i in range(4)]
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    sx, sy, sz = node.get("scale", [1, 1, 1])
    tx, ty, tz = node.get("translation", [0, 0, 0])
    return [[(1 - 2 * (y*y + z*z))*sx, 2*(x*y-z*w)*sy, 2*(x*z+y*w)*sz, tx],
            [2*(x*y+z*w)*sx, (1-2*(x*x+z*z))*sy, 2*(y*z-x*w)*sz, ty],
            [2*(x*z-y*w)*sx, 2*(y*z+x*w)*sy, (1-2*(x*x+y*y))*sz, tz], [0, 0, 0, 1]]


def selected_nodes(doc):
    if "scene" not in doc:
        return None
    nodes = doc.get("nodes", [])
    pending = [(index, identity()) for index in reversed(doc["scenes"][doc["scene"]].get("nodes", []))]
    selected, seen = [], set()
    while pending:
        index, parent = pending.pop()
        require(index not in seen, "Repeated/cyclic node in selected scene")
        seen.add(index)
        world = multiply(parent, local(nodes[index]))
        selected.append((index, world))
        pending.extend((child, world) for child in reversed(nodes[index].get("children", [])))
    return selected


def span(root, asset, doc, binary, view_index, relative, count):
    require(0 <= count <= MAX_SPAN, "Oracle span exceeds 2 MiB authoring bound")
    view = doc["bufferViews"][view_index]
    require(not any(name in view.get("extensions", {}) for name in ("EXT_meshopt_compression", "KHR_meshopt_compression")),
            "Compressed buffer view needs an independent decoded oracle")
    require(relative >= 0 and relative + count <= view["byteLength"], "Accessor escapes its bufferView")
    offset = view.get("byteOffset", 0) + relative
    buffer = doc["buffers"][view["buffer"]]
    uri = buffer.get("uri")
    if uri is None:
        require(binary is not None and offset + count <= binary[1], "Accessor escapes GLB BIN")
        with asset.open("rb") as stream:
            stream.seek(binary[0] + offset)
            data = stream.read(count)
    elif uri.startswith("data:"):
        header, payload = uri.split(",", 1)
        require(len(payload) <= 3 * MAX_SPAN, "Embedded buffer exceeds bounded oracle decoding")
        decoded = base64.b64decode(payload, validate=True) if ";base64" in header else unquote_to_bytes(payload)
        data = decoded[offset:offset + count]
    else:
        path = safe(root, external(root, asset, uri))
        require(offset + count <= buffer["byteLength"], "Accessor escapes declared external buffer")
        with path.open("rb") as stream:
            stream.seek(offset)
            data = stream.read(count)
    require(len(data) == count, "Truncated independent accessor span")
    return data


def positions(root, asset, doc, binary, index):
    accessor = doc["accessors"][index]
    require(accessor["type"] == "VEC3" and accessor["count"] <= MAX_ELEMENTS,
            "Position oracle requires <=100000 VEC3 elements")
    formats = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2), 5126: ("f", 4)}
    require(accessor["componentType"] in formats, "Unsupported POSITION oracle format")
    fmt, width = formats[accessor["componentType"]]
    count = accessor["count"]
    values = [[0.0, 0.0, 0.0] for _ in range(count)]
    if "bufferView" in accessor:
        view = doc["bufferViews"][accessor["bufferView"]]
        stride = view.get("byteStride", width * 3)
        raw = span(root, asset, doc, binary, accessor["bufferView"], accessor.get("byteOffset", 0),
                   (count - 1) * stride + width * 3 if count else 0)
        values = [list(struct.unpack_from("<" + fmt * 3, raw, row * stride)) for row in range(count)]
    if "sparse" in accessor:
        sparse = accessor["sparse"]
        indices = sparse["indices"]
        index_format, index_width = {5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4)}[indices["componentType"]]
        raw_indices = span(root, asset, doc, binary, indices["bufferView"], indices.get("byteOffset", 0), sparse["count"] * index_width)
        source = sparse["values"]
        raw_values = span(root, asset, doc, binary, source["bufferView"], source.get("byteOffset", 0), sparse["count"] * width * 3)
        for row in range(sparse["count"]):
            at = struct.unpack_from("<" + index_format, raw_indices, row * index_width)[0]
            require(at < count, "Sparse index outside position accessor")
            values[at] = list(struct.unpack_from("<" + fmt * 3, raw_values, row * width * 3))
    if accessor.get("normalized"):
        maximum = {5120: 127, 5121: 255, 5122: 32767, 5123: 65535}[accessor["componentType"]]
        values = [[max(-1, value / maximum) if accessor["componentType"] in (5120, 5122) else value / maximum
                   for value in point] for point in values]
    require(all(math.isfinite(value) for point in values for value in point), "Nonfinite source POSITION")
    return values


def geometry_oracle(root, asset, doc, binary):
    selected = selected_nodes(doc)
    if selected is None:
        return {"status": "selection_needed", "reason": "Valid asset has no default scene; explicit scene selection is required"}
    primitives = []
    materials = set()
    animated = bool(doc.get("animations"))
    deformed = False
    total_vertices = triangles = 0
    for index, world in selected:
        node = doc["nodes"][index]
        deformed |= "skin" in node or "weights" in node or "EXT_mesh_gpu_instancing" in node.get("extensions", {})
        if "mesh" not in node:
            continue
        mesh = doc["meshes"][node["mesh"]]
        for primitive in mesh["primitives"]:
            deformed |= bool(primitive.get("targets"))
            position = primitive.get("attributes", {}).get("POSITION")
            require(position is not None, "Selected primitive has no POSITION")
            count = doc["accessors"][position]["count"]
            indices = doc["accessors"][primitive["indices"]]["count"] if "indices" in primitive else count
            mode = primitive.get("mode", 4)
            total_vertices += count
            triangles += indices // 3 if mode == 4 else max(0, indices - 2) if mode in (5, 6) else 0
            materials.add(primitive.get("material", -1))
            primitives.append((position, world, primitive, mode))
    result = {"status": "partial", "selected_nodes": len(selected), "primitive_instances": len(primitives),
              "declared_position_elements": total_vertices,
              "triangle_count": triangles if all(p[3] in (4, 5, 6) for p in primitives) else None,
              "material_indices": sorted(materials), "animated": animated, "deformed": deformed,
              "triangle_count_is_diagnostic": True,
              "provenance": "Independent declared accessor topology count only; may include degenerates or Draco strip source topology. Not a universal strict native triangle-count oracle."}
    try:
        require(primitives and not animated and not deformed, "Animated/deformed/empty scenes need an independent posed oracle")
        require(total_vertices <= MAX_ELEMENTS, "Scene position count exceeds bounded oracle authoring")
        require(not any("KHR_draco_mesh_compression" in item[2].get("extensions", {}) for item in primitives),
                "Draco geometry needs an independent decoded oracle")
        minimum, maximum = [math.inf] * 3, [-math.inf] * 3
        cache = {}
        for index, world, _, _ in primitives:
            if index not in cache:
                cache[index] = positions(root, asset, doc, binary, index)
            for point in cache[index]:
                transformed = [sum(world[row][column] * (point + [1])[column] for column in range(4)) for row in range(3)]
                converted = [transformed[2] / .0254, transformed[0] / .0254, transformed[1] / .0254]
                for axis in range(3):
                    minimum[axis] = min(minimum[axis], converted[axis])
                    maximum[axis] = max(maximum[axis], converted[axis])
        require(all(math.isfinite(value) for value in minimum + maximum), "No finite independent scene bounds")
        result.update(bounds_min=minimum, bounds_max=maximum, bounds_status="available",
                      bounds_absolute_tolerance_inches=.02,
                      bounds_relative_tolerance=0.00005,
                      bounds_provenance="Independent bounded stdlib accessor/sparse decode and column-vector T*R*S hierarchy; agreed (z,x,y)/0.0254 conversion")
    except (ValueError, KeyError, IndexError, struct.error) as error:
        result.update(bounds_status="unverified", bounds_reason=str(error))
    return result


def features(doc, path, dependencies):
    result = {"core-gltf-external-embedded-and-glb", "container:" + path.suffix.lower()[1:]}
    result.add("storage:external" if dependencies else "storage:self-contained")
    for accessor in doc.get("accessors", []):
        result.add("accessor-component:" + str(accessor["componentType"]))
        if accessor.get("normalized"):
            result.add("accessor:normalized")
        if "sparse" in accessor:
            result.add("accessor:sparse")
        if accessor.get("byteOffset", 0):
            result.add("accessor:offset")
        if accessor.get("type", "").startswith("MAT"):
            result.add("accessor:matrix")
    if doc.get("accessors"):
        result.add("core-accessors-sparse-interleaved-normalized")
    if any(view.get("byteStride") for view in doc.get("bufferViews", [])):
        result.add("accessor:interleaved")
    for mesh in doc.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            result.update(("core-primitive-modes-index-formats-and-nonindexed", "core-mesh-attributes-uv-colors-tangents"))
            result.add("primitive-mode:" + str(primitive.get("mode", 4)))
            result.add("geometry:indexed" if "indices" in primitive else "geometry:nonindexed")
            result.update("attribute:" + name for name in primitive.get("attributes", {}))
            for target in primitive.get("targets", []):
                result.add("core-morph-targets-including-tangents")
                result.update("morph:" + name for name in target)
    if doc.get("nodes") or doc.get("cameras"):
        result.add("core-scenes-transforms-and-cameras")
    if "scene" not in doc:
        result.add("scene:no-default")
    if len(doc.get("scenes", [])) > 1:
        result.add("scene:multiple")
    if doc.get("cameras"):
        result.update("camera:" + camera["type"] for camera in doc["cameras"])
    if doc.get("skins"):
        result.add("core-skins-inverse-bind-poses-and-multiple-influences")
    for animation in doc.get("animations", []):
        result.add("core-animation-step-linear-cubicspline")
        result.update("animation:" + sampler.get("interpolation", "LINEAR") for sampler in animation.get("samplers", []))
        result.update("animation-target:" + channel["target"]["path"] for channel in animation.get("channels", []))
    if doc.get("materials") or doc.get("textures"):
        result.add("core-pbr-alpha-textures-color-space-samplers")
    for material in doc.get("materials", []):
        result.add("alpha:" + material.get("alphaMode", "OPAQUE"))
    result.update("extension-" + name for name in doc.get("extensionsUsed", []))
    return sorted(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=(Path(__file__).resolve().parents[4]/'research/gltf-validation') / "corpus-case-manifest.json")
    args = parser.parse_args()
    root = args.corpus.resolve(strict=True)
    require(git(root, "rev-parse", "--show-toplevel") == str(root) and git(root, "rev-parse", "HEAD") == PIN,
            "Corpus checkout/pin differs")
    require(not git(root, "status", "--porcelain", "--untracked-files=all", "--", "Models", "LICENSES"),
            "Corpus Models/LICENSES has local modifications")
    coverage = load_json((Path(__file__).resolve().parents[4]/'research/gltf-validation') / "coverage.json".read_text())
    selected_extensions = set(coverage["provisional_extensions"])
    index = load_json((root / "Models/model-index.json").read_text())
    files, cases, material_tables, binding_tables = {}, [], {}, {}
    def shared(table, value):
        canonical = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False)
        key = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
        table.setdefault(key, json.loads(canonical))
        return key
    def record(relative):
        path = safe(root, relative)
        if relative not in files:
            files[relative] = {"bytes": path.stat().st_size, "sha256": sha(path)}
        return path
    record("Models/model-index.json")
    licenses = sorted(str(path.relative_to(root)) for path in (root / "LICENSES").rglob("*") if path.is_file())
    for relative in licenses:
        record(relative)
    for model in index:
        name = model["name"]
        metadata_path, readme = f"Models/{name}/metadata.json", f"Models/{name}/README.md"
        metadata = load_json(record(metadata_path).read_text())
        record(readme)
        screenshot = model.get("screenshot")
        screenshot_path = f"Models/{name}/{screenshot}" if screenshot else None
        if screenshot_path:
            record(screenshot_path)
        for variant, filename in model["variants"].items():
            relative = f"Models/{name}/{variant}/{filename}"
            path = record(relative)
            case = {"id": name + "/" + variant, "path": relative, "model": name, "variant": variant,
                    "source_form": path.suffix.lower()[1:], "provenance": {"readme": readme, "metadata": metadata_path,
                    "legal": metadata.get("legal", []), "license_directory": "LICENSES"}, "upstream_defect": None}
            try:
                doc, binary = container(path)
                used, required = set(doc.get("extensionsUsed", [])), set(doc.get("extensionsRequired", []))
                require(required <= used, "Required extension is not declared used")
                outside_required = sorted(required - selected_extensions)
                dependencies = sorted({external(root, path, item["uri"]) for item in doc.get("buffers", []) + doc.get("images", [])
                                       if item.get("uri") and not item["uri"].startswith("data:")})
                for dependency in dependencies:
                    record(dependency)
                oracle = geometry_oracle(root, path, doc, binary)
                declared_elements = sum(accessor.get("count", 0) for accessor in doc.get("accessors", []))
                estimated = 16 * sum(files[item]["bytes"] for item in [relative] + dependencies) + declared_elements * 256
                material_ref = shared(material_tables, doc.get("materials", []))
                binding_ref = shared(binding_tables, {
                    "node_mesh_instances": [[i, node["mesh"]] for i, node in enumerate(doc.get("nodes", [])) if "mesh" in node],
                    "mesh_primitive_materials": [[mi, pi, primitive.get("material", -1)]
                        for mi, mesh in enumerate(doc.get("meshes", [])) for pi, primitive in enumerate(mesh["primitives"])],
                    "scene_roots": [scene.get("nodes", []) for scene in doc.get("scenes", [])]})
                case.update(classification="out_of_scope_required_extension" if outside_required else "applicable",
                            core_only=not used, extensions_used=sorted(used), extensions_required=sorted(required),
                            selected_extensions=sorted(used & selected_extensions),
                            unsupported_required_extensions=outside_required,
                            unsupported_optional_extensions=sorted(used - required - selected_extensions),
                            dependencies=dependencies, feature_ids=features(doc, path, dependencies),
                            default_scene=doc.get("scene"), geometry_oracle=oracle,
                            estimated_peak_bytes=estimated, estimate_is_hard_cap=False,
                            source_metadata={"scenes": len(doc.get("scenes", [])), "nodes": len(doc.get("nodes", [])),
                                             "meshes": len(doc.get("meshes", [])), "materials": len(doc.get("materials", [])),
                                             "animations": len(doc.get("animations", [])), "skins": len(doc.get("skins", []))},
                            material_contract_ref=material_ref, node_material_binding_ref=binding_ref,
                            animation_oracle={"status": "unverified" if doc.get("animations") or doc.get("skins") else "not_applicable",
                                              "reason": "Independent per-clip/key/posed native readback oracle not yet attached"},
                            visual_oracle={"status": "unverified", "reference_image": screenshot_path,
                                           "reason": "Upstream screenshot has no matched camera/light/exposure contract; it is review context, not a pixel oracle"},
                            required_forms=["original-source", "derived-packaging-glb_c"])
            except (ValueError, OSError, KeyError, IndexError, TypeError, struct.error) as error:
                case.update(classification="inventory_error", error=str(error), dependencies=[], feature_ids=[])
            cases.append(case)
    indexed = {case["path"] for case in cases}
    disk = {str(path.relative_to(root)) for path in (root / "Models").glob("*/glTF*/*")
            if path.is_file() and path.suffix.lower() in (".gltf", ".glb")}
    require(len(index) == 150 and len(cases) == 337 and indexed == disk, "337 indexed/disk asset inventory differs")
    matrix = {feature: {"applicable_cases": [case["id"] for case in cases if case["classification"] == "applicable" and feature in case["feature_ids"]],
                        "acceptance": "unverified", "dedicated_tests_required": True}
              for feature in coverage["required_coverage"]}
    manifest = {"version": 1, "corpus": {"repository": coverage["corpus"]["repository"], "commit": PIN,
                "index": "Models/model-index.json", "model_count": 150, "case_count": 337,
                "indexed_equals_disk": True, "models_and_licenses_clean_at_authoring": True},
                "selected_extensions": sorted(selected_extensions),
                "scope_policy": "Only required extensions outside the selected scope exclude a row. Optional outside-scope extensions test the core fallback; valid no-default-scene/unsupported core features remain applicable.",
                "oracle_policy": "No load-only or source-versus-compiled-only result proves work as intended. Missing independent geometry/material/animation/visual proof is explicit unverified coverage.",
                "numeric_oracle": {"builder": Path(__file__).name, "builder_sha256": sha(Path(__file__).resolve()),
                "method": "Python stdlib independent accessor/sparse decode, JSON topology counts and column-vector node transforms; no Sandbox.Gltf/native/reference-output calls",
                "max_span_bytes": MAX_SPAN, "max_position_elements": MAX_ELEMENTS,
                "specification": "https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html",
                "basis_contract": "glTF metres (x,y,z) become engine inches (z,x,y)/0.0254"},
                "license_files": licenses, "upstream_defects": [], "files": dict(sorted(files.items())),
                "material_contract_tables": dict(sorted(material_tables.items())),
                "node_material_binding_tables": dict(sorted(binding_tables.items())),
                "classification_counts": dict(Counter(case["classification"] for case in cases)),
                "feature_matrix": matrix, "cases": sorted(cases, key=lambda case: case["id"])}
    args.output.write_text(json.dumps(manifest, separators=(",", ":"), ensure_ascii=False, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps({"models": 150, "cases": len(cases), "classifications": manifest["classification_counts"],
                      "files": len(files), "output": str(args.output), "engine_validation_performed": False}))


if __name__ == "__main__":
    main()
