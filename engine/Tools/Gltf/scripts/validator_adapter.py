#!/usr/bin/env python3
"""Run the pinned Khronos CLI on a fresh sample inventory and compiled artifacts."""
import argparse
import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path, PurePosixPath
from urllib.parse import unquote, urlsplit

import validate

VALIDATOR_VERSION = "2.0.0-dev.3.10"
# Observed from the official Linux x64 release; upstream does not publish a digest.
VALIDATOR_SHA256 = "5cba1e097935c9efd929d24a49610824259b030f0ae45672273bfbcbb5b87a62"
VALIDATOR_RELEASE = "https://github.com/KhronosGroup/glTF-Validator/releases/tag/2.0.0-dev.3.10"
VALIDATOR_SOURCE_COMMIT = "bcd52cc4ba5f333b2999a58f67cc05ddf28b4fb1"


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_validator(path):
    if not path.is_file():
        raise ValueError("validator executable is missing")
    digest = sha256_file(path)
    if digest != VALIDATOR_SHA256:
        raise ValueError("validator executable hash does not match the pinned Linux x64 release")
    return digest


def parse_report(raw, returncode):
    report = validate.load_json(raw)
    if not isinstance(report, dict) or report.get("validatorVersion") != VALIDATOR_VERSION:
        raise ValueError("validator report has missing or unexpected version")
    issues = report.get("issues")
    if not isinstance(issues, dict):
        raise ValueError("validator report is missing issues")
    keys = ("numErrors", "numWarnings", "numInfos", "numHints")
    if any(type(issues.get(key)) is not int or issues[key] < 0 for key in keys):
        raise ValueError("validator issue counts are invalid")
    messages = issues.get("messages")
    if not isinstance(messages, list) or type(issues.get("truncated")) is not bool:
        raise ValueError("validator messages or truncation status are invalid")
    for message in messages:
        if not isinstance(message, dict) or not isinstance(message.get("code"), str) or type(message.get("severity")) is not int or message["severity"] not in range(4):
            raise ValueError("validator issue message is invalid")
    if not issues["truncated"]:
        actual = [sum(1 for item in messages if item["severity"] == severity) for severity in range(4)]
        if actual != [issues[key] for key in keys]:
            raise ValueError("validator message and issue counts differ")
    if (returncode == 0) != (issues["numErrors"] == 0):
        raise ValueError("validator process status and error count disagree")
    if issues["numErrors"]:
        status = "invalid"
    elif issues["truncated"] or any(item["code"] == "UNSUPPORTED_EXTENSION" for item in messages):
        status = "incomplete"
    else:
        status = "pass"
    return {"status": status, "issues": issues, "report": report}


def compiled_path(root, case_id):
    if not isinstance(case_id, str):
        raise ValueError("invalid case identifier")
    parts = case_id.split("/")
    if len(parts) != 2 or any(part in ("", ".", "..") or "\\" in part or "\x00" in part for part in parts):
        raise ValueError("invalid case identifier")
    root = root.resolve()
    current = root
    for part in (*parts, "model.glb_c"):
        current = current / part
        if current.is_symlink():
            raise ValueError("compiled artifact path contains a symbolic link")
    if not current.resolve().is_relative_to(root):
        raise ValueError("compiled artifact path escapes its root")
    return current


def validate_output_path(output, corpus, compiled_root, binary):
    output, corpus, binary = output.resolve(), corpus.resolve(), binary.resolve()
    if output == binary or output.is_relative_to(corpus) or (compiled_root is not None and output.is_relative_to(compiled_root.resolve())):
        raise ValueError("report output must not overwrite the validator or lie inside an input tree")


def compiler_receipt_index(receipt, expected_commit):
    if not isinstance(receipt, dict) or receipt.get("corpus_commit") != expected_commit or receipt.get("compile_requested") is not True:
        raise ValueError("compiler receipt must use the pinned corpus and a compile-enabled run")
    cases = receipt.get("cases")
    if not isinstance(cases, list):
        raise ValueError("compiler receipt has no case list")
    result = {}
    for case in cases:
        if not isinstance(case, dict) or not isinstance(case.get("id"), str) or case["id"] in result:
            raise ValueError("compiler receipt has an invalid or duplicate case")
        result[case["id"]] = case
    return result


def attest_compiled(case, artifact, source_hash, receipt):
    if receipt is None:
        return "not-provided"
    record = receipt.get(case["id"])
    if not isinstance(record, dict):
        return "missing-case"
    if not isinstance(source_hash, str) or not validate.SHA256.fullmatch(source_hash) or record.get("scope") != case["scope"] or record.get("source_sha256") != source_hash:
        return "source-or-scope-mismatch"
    stage = record.get("managed_container_roundtrip")
    if not isinstance(stage, dict) or stage.get("status") != "pass":
        return "compiler-stage-not-passing"
    if not artifact.is_file():
        return "missing-artifact"
    expected = record.get("artifact_sha256")
    if not isinstance(expected, str) or not validate.SHA256.fullmatch(expected) or sha256_file(artifact) != expected:
        return "artifact-hash-mismatch"
    return "pass"


def finish_attestation(attestation, result, record, artifact, package_root):
    if attestation != "pass":
        return attestation
    if result.get("artifact_sha256") != record.get("artifact_sha256"):
        return "artifact-changed-after-attestation"
    dependencies = record.get("packaged_dependency_sha256")
    if not isinstance(dependencies, dict):
        return "dependency-receipt-missing"
    expected = {}
    base = artifact.parent.relative_to(package_root)
    for name, digest in dependencies.items():
        if not isinstance(name, str) or not name or "\\" in name or "\x00" in name:
            return "invalid-dependency-receipt"
        parts = name.split("/")
        if any(part in ("", ".", "..") for part in parts) or PurePosixPath(name).is_absolute():
            return "invalid-dependency-receipt"
        if not isinstance(digest, str) or not validate.SHA256.fullmatch(digest):
            return "invalid-dependency-receipt"
        expected[(base / Path(*parts)).as_posix()] = digest
    if result.get("external_resource_sha256") != expected:
        return "dependency-hash-mismatch"
    return "pass"


def run_validator(binary, asset):
    process = subprocess.run([str(binary), "-o", "-r", str(asset)], capture_output=True, text=True, timeout=120, check=False)
    try:
        return parse_report(process.stdout, process.returncode)
    except (ValueError, TypeError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid validator response: {exc}; stderr: {process.stderr[:4096]}") from exc


def validate_source(binary, corpus, case):
    asset = validate.safe_file(corpus, case["path"])
    before = sha256_file(asset)
    resources = {}
    for resource in case.get("external_files", []):
        resources[resource] = sha256_file(validate.safe_file(corpus, resource))
    result = run_validator(binary, asset)
    if sha256_file(asset) != before or any(sha256_file(validate.safe_file(corpus, name)) != digest for name, digest in resources.items()):
        raise ValueError("source asset or resource changed during validation")
    result.update(asset_sha256=before, external_resource_sha256=resources)
    return result


def compiled_resources(root, artifact, report):
    root = root.resolve()
    info = report.get("info")
    if not isinstance(info, dict) or "resources" not in info:
        return {}, ["validator resource inventory is unavailable"]
    entries = info["resources"]
    if not isinstance(entries, list):
        return {}, ["validator resources are not a list"]
    hashes, problems = {}, []
    for entry in entries:
        if not isinstance(entry, dict) or entry.get("storage") != "external":
            continue
        uri = entry.get("uri")
        if not isinstance(uri, str):
            problems.append("external resource has no URI")
            continue
        parts = urlsplit(uri)
        if parts.scheme or parts.netloc or parts.query or parts.fragment:
            problems.append(f"external resource is not a local package path: {uri}")
            continue
        path = (artifact.parent / unquote(parts.path)).resolve()
        if not path.is_relative_to(root):
            problems.append(f"external resource escapes package root: {uri}")
        elif not path.is_file():
            problems.append(f"external resource missing from package: {uri}")
        else:
            hashes[str(path.relative_to(root))] = sha256_file(path)
    return hashes, sorted(set(problems))


def validate_compiled(binary, artifact, package_root=None):
    if not artifact.is_file():
        return {"status": "missing"}
    before = sha256_file(artifact)
    temporary = None
    try:
        # The CLI rejects .glb_c. Use the same directory to test external URIs
        # against the actual package location, not against the original source.
        with tempfile.NamedTemporaryFile(prefix=".gltf-validator-", suffix=".glb", dir=artifact.parent, delete=False) as stream:
            temporary = Path(stream.name)
            with artifact.open("rb") as source:
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    stream.write(chunk)
        if sha256_file(temporary) != before:
            raise ValueError("temporary compiled asset differs from its source")
        package_root = package_root or artifact.parent
        declared = []
        document_problem = []
        try:
            document = validate.read_asset(temporary)
            for entry in document.get("buffers", []) + document.get("images", []):
                uri = entry.get("uri")
                if uri is not None and not uri.lower().startswith("data:"):
                    declared.append({"storage": "external", "uri": uri})
        except (OSError, ValueError, TypeError, AttributeError) as exc:
            document_problem.append(f"compiled resource inventory failed: {exc}")
        hashes_before, problems_before = compiled_resources(package_root, artifact, {"info": {"resources": declared}})
        result = run_validator(binary, temporary)
        if sha256_file(artifact) != before:
            raise ValueError("compiled artifact changed during validation")
        hashes_after, problems_after = compiled_resources(package_root, artifact, {"info": {"resources": declared}})
        if hashes_before != hashes_after or problems_before != problems_after:
            raise ValueError("compiled dependencies changed during validation")
        reported_hashes, reported_problems = compiled_resources(package_root, artifact, result.get("report", {}))
        problems = sorted(set(document_problem + problems_before + reported_problems))
        if any(name not in hashes_before or hashes_before[name] != digest for name, digest in reported_hashes.items()):
            problems.append("validator reported a dependency that was not hashed before validation")
        if problems and result["status"] == "pass":
            result["status"] = "incomplete"
        result["artifact_sha256"] = before
        result["external_resource_sha256"] = hashes_before
        result["resource_issues"] = problems
        return result
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def summarize(cases, prefix, compiled_requested):
    required = [case for case in cases if case["scope"] == "required"]
    inventory_valid = all(case["scope"] != "error" for case in cases)
    source_selected = bool(required) and inventory_valid and all(case["source"]["status"] == "pass" for case in required)
    compiled_selected = compiled_requested and bool(required) and inventory_valid and all(
        case.get("compiled", {}).get("status") == "pass" and
        case.get("compiled", {}).get("fresh_compile_attestation") == "pass" for case in required)
    return {"selected_source_validation_pass": source_selected,
            "selected_compiled_validation_pass": compiled_selected if compiled_requested else False,
            "source_validation_pass": prefix is None and source_selected,
            "compiled_validation_pass": prefix is None and compiled_selected,
            "render_parity": "not-tested", "full_goal_pass": False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validator", type=Path, required=True)
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--compiled-root", type=Path)
    parser.add_argument("--compiler-report", type=Path, help="compile-enabled scanner report attesting source and artifact hashes")
    parser.add_argument("--case", help="optional case ID prefix for diagnostics")
    args = parser.parse_args(argv)
    try:
        binary, corpus, output = args.validator.resolve(), args.corpus.resolve(), args.output.resolve()
        compiled_root = args.compiled_root.resolve() if args.compiled_root else None
        validate_output_path(output, corpus, compiled_root, binary)
        if args.compiler_report and not compiled_root:
            raise ValueError("--compiler-report requires --compiled-root")
        if args.compiler_report and args.compiler_report.resolve() == output:
            raise ValueError("report output must not overwrite the compiler receipt")
        digest = verify_validator(binary)
        manifest = validate.load_json(validate.MANIFEST.read_text(encoding="utf-8"))
        validate.verify_checkout(corpus, manifest["corpus"])
        inventory = validate.inventory(corpus, manifest)
        receipt_bytes = args.compiler_report.read_bytes() if args.compiler_report else None
        receipt = validate.load_json(receipt_bytes) if receipt_bytes is not None else None
        receipt_index = compiler_receipt_index(receipt, manifest["corpus"]["commit"]) if receipt is not None else None
        selected = [case for case in inventory if args.case is None or case["id"].startswith(args.case)]
        if compiled_root and compiled_root.is_relative_to(corpus):
            raise ValueError("compiled root must be outside the corpus checkout")
        cases = []
        for case in selected:
            result = {"id": case["id"], "path": case["path"], "scope": case["scope"]}
            for field in ("exclusion_reason", "provenance_files", "extensions_used", "extensions_required"):
                if field in case:
                    result[field] = case[field]
            if case["scope"] == "error":
                result["source"] = {"status": "inventory-error", "error": case["error"]}
            else:
                try:
                    result["source"] = validate_source(binary, corpus, case)
                except (OSError, ValueError, subprocess.SubprocessError) as exc:
                    result["source"] = {"status": "error", "error": str(exc)}
            if compiled_root is not None:
                try:
                    artifact = compiled_path(compiled_root, case["id"])
                    source_sha = result["source"].get("asset_sha256")
                    attestation = attest_compiled(case, artifact, source_sha, receipt_index)
                    if receipt_index is not None and attestation != "pass":
                        result["compiled"] = {"status": "incomplete", "fresh_compile_attestation": attestation}
                    else:
                        result["compiled"] = validate_compiled(binary, artifact, compiled_root)
                        record = receipt_index[case["id"]] if receipt_index is not None else None
                        result["compiled"]["fresh_compile_attestation"] = finish_attestation(attestation, result["compiled"], record, artifact, compiled_root)
                except (OSError, ValueError, subprocess.SubprocessError) as exc:
                    result["compiled"] = {"status": "error", "error": str(exc)}
            cases.append(result)
        validate.verify_checkout(corpus, manifest["corpus"])
        if verify_validator(binary) != digest:
            raise ValueError("validator executable changed during validation")
        if receipt_bytes is not None and args.compiler_report.read_bytes() != receipt_bytes:
            raise ValueError("compiler receipt changed during validation")
        summary = summarize(cases, args.case, compiled_root is not None)
        result = {"validator_version": VALIDATOR_VERSION, "validator_sha256": digest,
                  "validator_release": VALIDATOR_RELEASE, "validator_source_commit": VALIDATOR_SOURCE_COMMIT,
                  "validator_digest_provenance": "observed official release binary; upstream published no digest",
                  "validator_limitations": "unsupported extensions are incomplete; visual and runtime conformance are not assessed",
                  "corpus_commit": manifest["corpus"]["commit"], "total_indexed_cases": len(inventory),
                  "compiler_report_sha256": hashlib.sha256(receipt_bytes).hexdigest() if receipt_bytes is not None else None,
                  "compiled_freshness": "source-artifact-and-packaged-dependency-hash-receipt" if receipt_bytes is not None else "unattested-diagnostic",
                  "selected_cases": len(cases), "selection": args.case, "summary": summary, "cases": cases}
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        ok = summary["selected_source_validation_pass"] and (compiled_root is None or summary["selected_compiled_validation_pass"])
        return 0 if ok else 1
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as exc:
        print(json.dumps({"full_goal_pass": False, "fatal": str(exc)}), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
