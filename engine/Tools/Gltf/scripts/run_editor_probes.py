#!/usr/bin/env python3
"""Run explicitly named existing editor probes; retain evidence, not a generic pass.

Root must guard the entire foreground command and verify the desktop editor.
Only byte-identical editor_*.csx files in this directory can be dispatched. No
editor is launched or killed, no environment is changed, and returned probe
fields are preserved without assuming they share acceptance semantics.
"""
import argparse
import json
import re
import shutil
import stat
import sys
from pathlib import Path

from run_editor_tests import (CliSession, bounded, check_status, decoded, digest,
                              discover, field, require, save, tool_value,
                              validate_arguments)


def load_probes(directory, names):
    require(names and len(names) <= 64 and len(set(names)) == len(names),
            "Provide 1-64 distinct existing probe filenames explicitly")
    probes = []
    for name in names:
        require(re.fullmatch(r"editor_[a-z0-9_]+\.csx", name),
                f"Expected a bare editor_*.csx filename: {name}")
        path = (directory / name).resolve(strict=True)
        require(path.parent == directory and path.is_file(),
                f"Probe must be an existing file directly in {directory}: {name}")
        source = bounded(path, 8 * 1024 * 1024)
        probes.append({"name": name, "path": path, "source": source,
                       "sha256": digest(source)})
    return probes


def model_request(path, probes):
    require(any(probe["name"] == "editor_model_load.csx" for probe in probes),
            "--model-request requires editor_model_load.csx in the explicit list")
    raw = bounded(path, 4096)
    value = decoded(raw)
    require(isinstance(value, dict) and set(value) == {
        "version", "request_id", "selection", "render", "capture_renderer", "camera_clear"},
        "Model request must contain exactly the six existing model-probe controls")
    require(type(value["version"]) is int and value["version"] == 1,
            "Expected model request version 1")
    require(isinstance(value["request_id"], str) and
            re.fullmatch(r"[0-9a-f]{32}", value["request_id"]), "Invalid model request ID")
    require(value["selection"] == "all" and value["render"] is True,
            "Only the existing render-all model request is supported")
    require(value["capture_renderer"] in ("native", "managed") and
            type(value["camera_clear"]) is bool, "Invalid model capture controls")
    return raw, value


def corpus_request(path, probes, directory):
    require(any(probe["name"] == "editor_corpus.csx" for probe in probes),
            "--corpus-request requires editor_corpus.csx in the explicit list")
    raw = bounded(path, 4096)
    value = decoded(raw)
    require(isinstance(value, dict) and type(value.get("version")) is int and value["version"] in (1, 2),
            "Expected corpus request version 1 or 2")
    fields = {"version", "request_id", "manifest_sha256", "case_ids",
              "max_estimated_bytes", "max_file_bytes", "keep_inputs"}
    if value["version"] == 2:
        fields.add("capture_renderer")
    require(set(value) == fields, "Corpus request fields do not match its version")
    if value["version"] == 2:
        require(value["capture_renderer"] in ("native", "managed"), "Invalid corpus capture renderer")
    require(isinstance(value["request_id"], str) and
            re.fullmatch(r"[0-9a-f]{32}", value["request_id"]), "Invalid corpus request ID")
    manifest_raw = bounded(Path(__file__).resolve().parents[4] / "research/gltf-validation/corpus-case-manifest.json", 32 * 1024 * 1024)
    require(isinstance(value["manifest_sha256"], str) and
            re.fullmatch(r"[0-9a-f]{64}", value["manifest_sha256"]) and
            value["manifest_sha256"] == digest(manifest_raw),
            "Corpus request manifest SHA-256 differs from the current manifest")
    manifest = decoded(manifest_raw)
    require(isinstance(manifest, dict) and manifest.get("version") == 1 and
            isinstance(manifest.get("cases"), list), "Invalid corpus manifest")
    ids = [item.get("id") for item in manifest["cases"] if isinstance(item, dict)]
    require(len(ids) == len(manifest["cases"]) and
            all(isinstance(item, str) and item for item in ids) and len(set(ids)) == len(ids),
            "Corpus manifest has invalid or duplicate case IDs")
    requested = value["case_ids"]
    require(isinstance(requested, list) and len(requested) <= len(ids) and
            all(isinstance(item, str) and item for item in requested) and
            len(set(requested)) == len(requested) and set(requested).issubset(ids),
            "Corpus request contains invalid, duplicate, or unknown case IDs")
    require(type(value["max_estimated_bytes"]) is int and
            1024 * 1024 <= value["max_estimated_bytes"] <= 2 * 1024 * 1024 * 1024 and
            type(value["max_file_bytes"]) is int and
            1024 * 1024 <= value["max_file_bytes"] <= 256 * 1024 * 1024 and
            type(value["keep_inputs"]) is bool, "Invalid corpus request resource controls")
    return raw, value


def scene_snapshot(status):
    return {name: field(status, name) for name in (
        "ActiveScene", "ActiveScenePath", "SceneHasUnsavedChanges", "IsPlaying")}


def archive_previous_outputs(checkout, probe_name, output, budget):
    """Preserve legacy fixed-output trees before a probe can overwrite them."""
    root = (checkout / "Temp/gltf-validation").resolve()
    if not root.is_dir():
        return []
    prefixes = [probe_name[:-4].replace("_", "-")]
    if probe_name == "editor_metalness_lighting.csx":
        prefixes.append("editor-lighting-controls")
    archived = []
    for source in sorted(root.iterdir()):
        if not any(source.name == prefix or source.name.startswith(prefix + "-") for prefix in prefixes):
            continue
        # UUID-based probes have no top-level receipt and need no archival copy.
        if not source.is_dir() or not (source / "receipt.json").is_file():
            continue
        require(not source.is_symlink(), "Refusing a symbolic-link output directory")
        files = []
        directories = []
        size = 0
        entries = 0
        for path in source.rglob("*"):
            entries += 1
            require(entries <= 16384, "Previous output tree exceeds the bounded entry inventory")
            require(not path.is_symlink(), f"Refusing a symbolic link in previous evidence: {path}")
            require(path.resolve().is_relative_to(root), "Previous evidence escapes its output tree")
            if path.is_dir():
                directories.append(path.relative_to(source))
                continue
            info = path.stat()
            require(stat.S_ISREG(info.st_mode), f"Previous evidence is not a regular file: {path}")
            require(info.st_size <= 64 * 1024 * 1024, f"Previous evidence file exceeds 64 MiB: {path}")
            files.append((path, info))
            size += info.st_size
            require(budget["files"] + len(files) <= 8192 and budget["bytes"] + size <= 512 * 1024 * 1024,
                    "Previous-output archival exceeds the 8192-file/512-MiB batch bound; no probe dispatched")
        destination = output / "previous-outputs" / source.name
        destination.mkdir(parents=True, exist_ok=False)
        for relative in directories:
            (destination / relative).mkdir(parents=True, exist_ok=True)
        manifest = []
        for path, before in files:
            target = destination / path.relative_to(source)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
            after = path.stat()
            require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) ==
                    (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns),
                    f"Previous output changed while being archived: {path}")
            raw = bounded(target) if before.st_size else b""
            manifest.append({"path": str(path.relative_to(source)), "bytes": before.st_size,
                             "sha256": digest(raw)})
        save(destination.parent / (source.name + ".archive-manifest.json"), manifest)
        budget["files"] += len(files)
        budget["bytes"] += size
        archived.append({"source": str(source), "copy": str(destination),
                         "files": len(files), "bytes": size})
    return archived


def result_value(result):
    """Preserve non-JSON/script-specific returns without inventing acceptance."""
    if "structuredContent" in result:
        return result["structuredContent"]
    content = result.get("content", [])
    if len(content) == 1 and content[0].get("type") == "text":
        try:
            return decoded(content[0]["text"].encode("utf-8"))
        except ValueError:
            return {"text": content[0]["text"]}
    return {"content": content}


def copy_receipts(value, checkout, exchange, output):
    """Copy only receipt.json files within the project evidence tree/session."""
    if not isinstance(value, dict):
        return []
    candidates = []
    for key, item in value.items():
        if not isinstance(item, str):
            continue
        normalized = key.replace("_", "").casefold()
        if normalized in ("receiptpath", "path"):
            candidates.append(item)
        elif normalized == "output":
            candidates.append(str(Path(item) / "receipt.json"))
    retained = []
    for candidate in dict.fromkeys(candidates):
        observation = {"returned_path": candidate, "copied": False}
        try:
            path = Path(candidate)
            require(path.is_absolute() and path.name == "receipt.json",
                    "Returned path is not an absolute receipt.json path")
            path = path.resolve(strict=True)
            evidence_root = (checkout / "Temp/gltf-validation").resolve()
            require(path.is_relative_to(evidence_root) or path.is_relative_to(exchange.resolve()),
                    "Returned receipt resolves outside the permitted evidence/session directories")
            require(path.is_file(), "Returned receipt is not a regular file")
            raw = bounded(path)
            destination = output / f"receipt-{len(retained) + 1:02}.json"
            destination.write_bytes(raw)
            observation.update(copied=True, resolved_path=str(path), copy=str(destination),
                               sha256=digest(raw))
        except (OSError, ValueError) as error:
            observation["error"] = f"{type(error).__name__}: {error}"
        retained.append(observation)
    return retained


def invoke_probe(session, tool, copied_path, output):
    arguments = {"path": str(copied_path)}
    validate_arguments(tool, arguments)
    result = session.request("tools/call", {"name": tool["name"], "arguments": arguments})
    reply = session.output / f"reply-{session.incoming:03}.json"
    (output / "reply.json").write_bytes(bounded(reply))
    require(isinstance(result, dict), "Probe returned an invalid MCP result")
    save(output / "tool-result.json", result)
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("probes", nargs="+", help="Explicit bare editor_*.csx filenames, in execution order")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--checkout", type=Path, default=Path(__file__).resolve().parents[4])
    parser.add_argument("--cli", type=Path)
    parser.add_argument("--output", type=Path, required=True, help="A new artifact directory")
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--confirmed-discovery", type=Path, required=True)
    parser.add_argument("--confirmed-sha256", required=True)
    parser.add_argument("--model-request", type=Path,
                        help="Existing six-field gltf-model-request.json selecting render-all only")
    parser.add_argument("--corpus-request", type=Path,
                        help="Version 1 or 2 gltf-corpus-request.json for editor_corpus.csx")
    args = parser.parse_args(argv)
    session = None
    created = False
    summary = {"transport_completed": False, "evaluates_probe_pass": False,
               "probes": [], "scope": "Existing probe execution evidence; review each probe's own semantics"}
    output = args.output.resolve()
    try:
        require(args.pid > 0 and 1 <= args.timeout <= 86400, "Invalid PID or timeout")
        checkout = args.checkout.resolve(strict=True)
        project = args.project.resolve(strict=True)
        require(project.is_file() and project.suffix == ".sbproj", "Expected an existing .sbproj file")
        directory = (checkout / "engine/Tools/Gltf/editor").resolve(strict=True)
        require(directory == Path(__file__).resolve().parents[1] / "editor",
                "Probe directory must be this executor's own checked-out directory")
        cli = (args.cli or checkout / "game/bin/managed/sbox-cli").resolve(strict=True)
        require(cli.is_file(), "Installed CLI is not a file")
        probes = load_probes(directory, args.probes)
        request_raw, request_value = model_request(args.model_request, probes) if args.model_request else (None, None)
        corpus_raw, corpus_value = corpus_request(args.corpus_request, probes, directory) if args.corpus_request else (None, None)
        approved_raw = bounded(args.confirmed_discovery)
        require(re.fullmatch(r"[0-9a-f]{64}", args.confirmed_sha256) and
                digest(approved_raw) == args.confirmed_sha256, "Reviewed discovery hash differs")
        approved = decoded(approved_raw)
        require(approved.get("version") == 1 and approved.get("mode") == "discover" and
                approved.get("tests_dispatched") is False and approved.get("pid") == args.pid and
                approved.get("project") == str(project), "Reviewed discovery identifies another editor")
        output.mkdir(parents=True, exist_ok=False)
        created = True
        inputs = {"probe_source_sha256": {probe["name"]: probe["sha256"] for probe in probes},
                  "cli_sha256": digest(bounded(cli)),
                  "executor_sha256": digest(bounded(Path(__file__).resolve(), 1024 * 1024)),
                  "transport_sha256": digest(bounded(Path(__file__).resolve().parent / "run_editor_tests.py", 1024 * 1024))}
        save(output / "inputs.json", inputs)
        session = CliSession(cli, args.pid, output, args.timeout)
        live, exchange = discover(session, args.pid, project, inputs, output)
        require(live["tools"] == approved.get("tools"), "Live tool schemas changed; root must review discovery again")
        if request_raw is not None:
            request_path = exchange / "gltf-model-request.json"
            with request_path.open("xb") as stream:
                stream.write(request_raw)
            require(bounded(request_path, 4096) == request_raw, "Model request copy differs")
            (output / "gltf-model-request.json").write_bytes(request_raw)
            summary["model_request"] = {"path": str(request_path), "sha256": digest(request_raw),
                                        "values": request_value}
        if corpus_raw is not None:
            corpus_path = exchange / "gltf-corpus-request.json"
            with corpus_path.open("xb") as stream:
                stream.write(corpus_raw)
            require(bounded(corpus_path, 4096) == corpus_raw, "Corpus request copy differs")
            archived_corpus = output / "gltf-corpus-request.json"
            archived_corpus.write_bytes(corpus_raw)
            require(bounded(archived_corpus, 4096) == corpus_raw, "Archived corpus request copy differs")
            summary["corpus_request"] = {"path": str(corpus_path), "archive_path": str(archived_corpus),
                                         "sha256": digest(corpus_raw), "values": corpus_value}
        archive_budget = {"files": 0, "bytes": 0}
        for index, probe in enumerate(probes, 1):
            require(session.pending is None and session.process.poll() is None,
                    "No responsive idle CLI connection remains")
            probe_output = output / f"probe-{index:02}-{probe['name'][:-4]}"
            probe_output.mkdir()
            record = {"name": probe["name"], "source_sha256": probe["sha256"],
                      "status": "not_dispatched", "output": str(probe_output)}
            summary["probes"].append(record)
            before = tool_value(session.call(live["tools"]["editor_status"], {}))
            save(probe_output / "status-before.json", before)
            check_status(before, project)
            record["scene_before"] = scene_snapshot(before)
            require(digest(bounded(probe["path"], 8 * 1024 * 1024)) == probe["sha256"],
                    f"Probe source changed before dispatch: {probe['name']}")
            record["prior_outputs"] = archive_previous_outputs(checkout, probe["name"], probe_output, archive_budget)
            copied = exchange / f"probe-{index:02}-{probe['name']}"
            with copied.open("xb") as stream:
                stream.write(probe["source"])
            require(bounded(copied, 8 * 1024 * 1024) == probe["source"], "Probe script copy differs")
            record["script_path"] = str(copied)
            incoming_before = session.incoming
            try:
                result = invoke_probe(session, live["tools"]["run_csharp"], copied, probe_output)
                record["status"] = "tool_error" if result.get("isError") is True else "tool_returned"
                if record["status"] == "tool_returned":
                    value = result_value(result)
                    save(probe_output / "returned-value.json", value)
                    record["receipts"] = copy_receipts(value, checkout, exchange, probe_output)
            except (OSError, ValueError, KeyError, TypeError, TimeoutError) as error:
                record.update(status="request_error", error=f"{type(error).__name__}: {error}")
            finally:
                if session.incoming > incoming_before:
                    reply = session.output / f"reply-{session.incoming:03}.json"
                    record["last_reply_path"] = str(reply)
                    if not (probe_output / "reply.json").exists():
                        (probe_output / "reply.json").write_bytes(bounded(reply))
                save(probe_output / "observation.json", record)
            # Compilation/runtime tool errors are retained and may be followed by
            # another probe only after these independent responsiveness checks.
            require(session.pending is None and session.process.poll() is None,
                    f"Probe left an outstanding/closed connection: {probe['name']}")
            console = session.call(live["tools"]["read_console"], {})
            save(probe_output / "console.json", console)
            after = tool_value(session.call(live["tools"]["editor_status"], {}))
            save(probe_output / "status-after.json", after)
            record["scene_after"] = scene_snapshot(after)
            record["scene_changed"] = record["scene_before"] != record["scene_after"]
            # Human scene edits are observations, never a reason to fail a probe.
            check_status(after, project)
            record["source_changed_during_run"] = digest(bounded(probe["path"], 8 * 1024 * 1024)) != probe["sha256"]
            save(probe_output / "observation.json", record)
            save(output / "summary.json", summary)
            print(json.dumps({"probe": probe["name"], "status": record["status"],
                              "scene_changed": record["scene_changed"],
                              "artifact": str(probe_output)}, separators=(",", ":")), flush=True)
        summary["transport_completed"] = True
    except (OSError, ValueError, KeyError, TypeError, TimeoutError, KeyboardInterrupt) as error:
        summary["error"] = f"{type(error).__name__}: {error}"
    finally:
        if session is not None:
            summary["cli_exit"] = session.close()
            if summary["cli_exit"]["exit_code"] != 0 or summary["cli_exit"]["interrupted_request"] is not None:
                summary["transport_completed"] = False
                summary["error"] = summary.get("error", "CLI did not finish cleanly")
        summary["tool_errors"] = sum(item["status"] in ("tool_error", "request_error") for item in summary["probes"])
        if created:
            save(output / "summary.json", summary)
        print(json.dumps({"transport_completed": summary["transport_completed"],
                          "evaluates_probe_pass": False, "observed_probes": len(summary["probes"]),
                          "tool_errors": summary["tool_errors"], "error": summary.get("error"),
                          "summary_path": str(output / "summary.json")}, separators=(",", ":")), flush=True)
    return 0 if summary["transport_completed"] and not summary["tool_errors"] else 1


if __name__ == "__main__":
    sys.exit(main())
