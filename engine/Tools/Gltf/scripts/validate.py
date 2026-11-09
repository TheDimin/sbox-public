#!/usr/bin/env python3
"""Inventory a pinned sample checkout and aggregate independent evidence.

This is not a glTF validator or an s&box adapter. See README.md.
"""
import argparse
import hashlib
import json
import re
import struct
import subprocess
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit

HERE = Path(__file__).resolve().parent
MANIFEST = (Path(__file__).resolve().parents[4]/'research/gltf-validation') / "coverage.json"
CATEGORIES = ("parser", "numeric", "render", "compiled")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
LFS_HEADER = b"version https://git-lfs.github.com/spec/v1"


def no_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_json(content):
    return json.loads(content, object_pairs_hook=no_duplicate_keys)


def git(root, *args):
    result = subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True, check=False)
    if result.returncode:
        raise ValueError("git " + " ".join(args) + ": " + result.stderr.strip())
    return result.stdout.strip()


def verify_checkout(root, spec):
    if not root.is_dir():
        raise ValueError("corpus directory is missing")
    if git(root, "rev-parse", "--show-toplevel") != str(root.resolve()):
        raise ValueError("corpus must be the root of its own git checkout")
    if git(root, "rev-parse", "HEAD") != spec["commit"]:
        raise ValueError("corpus HEAD does not match the manifest pin")
    dirty = git(root, "status", "--porcelain", "--untracked-files=all", "--", "Models", "LICENSES")
    if dirty:
        raise ValueError("corpus Models or LICENSES has local changes or untracked files")


def safe_file(root, relative):
    p = (root / relative).resolve()
    if not p.is_relative_to(root.resolve()):
        raise ValueError(f"path escapes corpus: {relative}")
    if not p.is_file():
        raise ValueError(f"missing file: {relative}")
    with p.open("rb") as f:
        if f.read(len(LFS_HEADER)) == LFS_HEADER:
            raise ValueError(f"unfetched Git LFS object: {relative}")
    return p


def read_asset(path):
    if path.suffix.lower() == ".gltf":
        with path.open(encoding="utf-8") as f:
            return load_json(f.read())
    if path.suffix.lower() != ".glb":
        raise ValueError("unexpected indexed asset extension")
    with path.open("rb") as f:
        header = f.read(12)
        if len(header) != 12:
            raise ValueError("truncated GLB header")
        magic, version, length = struct.unpack("<4sII", header)
        if magic != b"glTF" or version != 2 or length != path.stat().st_size:
            raise ValueError("invalid GLB header or length")
        chunk = f.read(8)
        if len(chunk) != 8:
            raise ValueError("missing GLB JSON chunk")
        size, chunk_type = struct.unpack("<II", chunk)
        if chunk_type != 0x4E4F534A or size % 4 or size > length - 20:
            raise ValueError("invalid GLB JSON chunk")
        raw = f.read(size)
        if len(raw) != size:
            raise ValueError("truncated GLB JSON chunk")
        return load_json(raw.decode("utf-8"))


def external_files(root, asset, doc):
    paths = []
    for entry in doc.get("buffers", []) + doc.get("images", []):
        uri = entry.get("uri")
        if uri is None or uri.startswith("data:"):
            continue
        parts = urlsplit(uri)
        if parts.scheme or parts.netloc or parts.query or parts.fragment:
            raise ValueError(f"unsupported external URI: {uri}")
        path = safe_file(root, asset.parent.relative_to(root) / unquote(parts.path))
        paths.append(str(path.relative_to(root)))
    return sorted(set(paths))


def referenced_extensions(value):
    found = set()
    if isinstance(value, dict):
        if "extensions" in value:
            extensions = value["extensions"]
            if not isinstance(extensions, dict):
                raise ValueError("extensions property is not an object")
            found.update(extensions)
        for key, child in value.items():
            if key != "extras":
                found.update(referenced_extensions(child))
    elif isinstance(value, list):
        for child in value:
            found.update(referenced_extensions(child))
    return found


def inventory(root, manifest):
    index = load_json(safe_file(root, manifest["corpus"]["index"]).read_text(encoding="utf-8"))
    if not isinstance(index, list) or not index:
        raise ValueError("empty or invalid corpus index")
    cases = []
    seen = set()
    seen_models = set()
    seen_files = set()
    for model in index:
        name, variants = model["name"], model["variants"]
        if not isinstance(name, str) or not name or name in seen_models or Path(name).name != name or name in (".", ".."):
            raise ValueError(f"invalid or duplicate model name: {name}")
        seen_models.add(name)
        if not isinstance(variants, dict) or not variants:
            raise ValueError(f"missing variants for {name}")
        for variant, filename in variants.items():
            if not isinstance(variant, str) or not variant or Path(variant).name != variant or variant in (".", ".."):
                raise ValueError(f"invalid variant name: {variant}")
            if not isinstance(filename, str) or not filename or Path(filename).name != filename or filename in (".", ".."):
                raise ValueError(f"invalid asset filename: {filename}")
            case_id = f"{name}/{variant}"
            if case_id in seen:
                raise ValueError(f"duplicate variant: {case_id}")
            seen.add(case_id)
            record = {"id": case_id, "path": f"Models/{name}/{variant}/{filename}", "tags": model.get("tags", [])}
            try:
                asset = safe_file(root, record["path"])
                if asset in seen_files:
                    raise ValueError("multiple indexed variants resolve to the same file")
                seen_files.add(asset)
                doc = read_asset(asset)
                if not isinstance(doc, dict):
                    raise ValueError("asset JSON is not an object")
                used = doc.get("extensionsUsed", [])
                required = doc.get("extensionsRequired", [])
                if not isinstance(used, list) or not isinstance(required, list) or not all(isinstance(x, str) for x in used + required):
                    raise ValueError("invalid extension lists")
                if not set(required).issubset(used):
                    raise ValueError("extensionsRequired is not a subset of extensionsUsed")
                if len(set(used)) != len(used) or len(set(required)) != len(required):
                    raise ValueError("duplicate extension name")
                undeclared = referenced_extensions(doc) - set(used)
                if undeclared:
                    raise ValueError("extension objects missing from extensionsUsed: " + ", ".join(sorted(undeclared)))
                record.update(extensions_used=sorted(set(used)), extensions_required=sorted(set(required)),
                              external_files=external_files(root, asset, doc),
                              unselected_extensions=sorted(set(used) - set(manifest["provisional_extensions"])))
                model_root = root / "Models" / name
                record["provenance_files"] = [str(p.relative_to(root)) for p in (model_root / "README.md", model_root / "metadata.json") if p.is_file()]
                if not record["provenance_files"]:
                    raise ValueError("missing model README and metadata; license provenance unavailable")
                record["scope"] = "excluded" if record["unselected_extensions"] else "required"
                if record["unselected_extensions"]:
                    record["exclusion_reason"] = "Uses unselected extensions: " + ", ".join(record["unselected_extensions"])
            except (OSError, ValueError, TypeError, KeyError, AttributeError, json.JSONDecodeError) as exc:
                record.update(scope="error", error=str(exc))
            cases.append(record)
    indexed = {case["path"] for case in cases}
    for path in (root / "Models").glob("*/glTF*/*"):
        if path.is_file() and path.suffix.lower() in (".gltf", ".glb"):
            relative = str(path.relative_to(root))
            if relative not in indexed:
                raise ValueError(f"unindexed asset variant: {relative}")
    return sorted(cases, key=lambda x: x["id"])


def proof_status(proof, base, category):
    if not isinstance(proof, dict):
        return "missing"
    if proof.get("result") != "pass":
        return "failed-or-incomplete"
    if proof.get("fallback") is not False:
        return "fallback-or-unspecified"
    adapter = proof.get("adapter")
    if not isinstance(adapter, dict) or not all(isinstance(adapter.get(k), str) and adapter[k].strip() for k in ("name", "version")):
        return "missing-adapter"
    if category == "render" and not all(isinstance(proof.get(k), str) and proof[k].strip() for k in ("reviewer", "reference", "conditions")):
        return "missing-visual-review"
    if category == "performance" and not all(isinstance(proof.get(k), str) and proof[k].strip() for k in ("hardware", "thresholds", "metrics")):
        return "missing-benchmark-context"
    artifacts = proof.get("artifacts")
    if not isinstance(artifacts, list) or not artifacts:
        return "missing-artifacts"
    for item in artifacts:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str) or not SHA256.fullmatch(str(item.get("sha256", ""))):
            return "invalid-artifact"
        try:
            path = (base / item["path"]).resolve()
            if not path.is_relative_to(base.resolve()) or not path.is_file():
                return "missing-artifact"
            digest = hashlib.sha256()
            with path.open("rb") as f:
                for chunk in iter(lambda: f.read(1024 * 1024), b""):
                    digest.update(chunk)
            if digest.hexdigest() != item["sha256"]:
                return "artifact-hash-mismatch"
        except (OSError, ValueError):
            return "missing-artifact"
    # Hashes establish integrity, not that an adapter was actually executed.
    # All adapter integrations are pending; replacing this gate requires a
    # reviewed executable integration and its own provenance/validation tests.
    return "adapter-integration-pending"


def assess(cases, manifest, evidence, base):
    ids = [case["id"] for case in cases]
    if len(set(ids)) != len(ids):
        raise ValueError("duplicate case IDs")
    if any(case.get("scope") not in ("required", "excluded", "error") for case in cases):
        raise ValueError("unknown case scope")
    if evidence.get("corpus_commit") != manifest["corpus"]["commit"]:
        evidence = {}
    by_case = evidence.get("cases", {})
    coverage = evidence.get("coverage", {})
    for case in cases:
        proofs = by_case.get(case["id"], {}) if isinstance(by_case, dict) else {}
        if not isinstance(proofs, dict):
            proofs = {}
        case["proofs"] = {category: proof_status(proofs.get(category), base, category) for category in CATEGORIES}
    requirements = {key: proof_status(coverage.get(key), base, "coverage") for key in manifest["required_coverage"]} if isinstance(coverage, dict) else {key: "missing" for key in manifest["required_coverage"]}
    performance = proof_status(evidence.get("performance"), base, "performance")
    required = [case for case in cases if case["scope"] == "required"]
    common = bool(required) and all(case["scope"] != "error" for case in cases) and all(v == "pass" for v in requirements.values()) and performance == "pass"
    applicable = common and all(all(status == "pass" for status in case["proofs"].values()) for case in required)
    corpus = applicable and all(case["scope"] == "required" for case in cases)
    return {"applicable_full_pass": applicable, "corpus_full_pass": corpus, "cases": cases, "coverage": requirements, "performance": performance}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", type=Path, help="checkout of the pinned sample repository")
    parser.add_argument("--evidence", type=Path, help="adapter evidence JSON; paths relative to its directory")
    parser.add_argument("--coverage-status", action="store_true", help="report pending integrations without a corpus")
    args = parser.parse_args(argv)
    try:
        manifest = load_json(MANIFEST.read_text(encoding="utf-8"))
        if args.coverage_status:
            print(json.dumps({"applicable_full_pass": False, "corpus_full_pass": False,
                              "completion_platform": manifest["completion_platform"],
                              "renderer_target": manifest["renderer_target"],
                              "case_proofs": {key: "adapter-integration-pending" for key in CATEGORIES},
                              "coverage": {key: "adapter-integration-pending" for key in manifest["required_coverage"]},
                              "performance": "adapter-integration-pending"}, indent=2, sort_keys=True))
            return 1
        if args.corpus is None:
            raise ValueError("--corpus is required unless --coverage-status is used")
        root = args.corpus.resolve()
        verify_checkout(root, manifest["corpus"])
        cases = inventory(root, manifest)
        evidence = load_json(args.evidence.read_text(encoding="utf-8")) if args.evidence else {}
        if not isinstance(evidence, dict):
            raise ValueError("evidence must be an object")
        result = assess(cases, manifest, evidence, args.evidence.resolve().parent if args.evidence else HERE)
        result["corpus_commit"] = manifest["corpus"]["commit"]
        result["completion_platform"] = manifest["completion_platform"]
        result["renderer_target"] = manifest["renderer_target"]
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0 if result["applicable_full_pass"] else 1
    except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as exc:
        print(json.dumps({"applicable_full_pass": False, "corpus_full_pass": False, "fatal": str(exc)}, indent=2))
        return 2


if __name__ == "__main__":
    sys.exit(main())
