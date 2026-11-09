#!/usr/bin/env python3
"""One coordinator-guarded attachment; discovery first, one isolated public case after review."""
import argparse
import json
from pathlib import Path
import sys

CHECKOUT = Path(__file__).resolve().parents[6]
BASE = CHECKOUT / "research/gltf-validation"
OWNED = Path(__file__).resolve().parent
FIXTURE_SHA256 = "7424187e9bd7cecb20b6d9d94bca4c098bb1ca28b3aa184b0db336a5dc97d958"
REQUEST_SHA256 = "f6e9b2a3d7c77388458d6c7a00a81c80366d227fbde34aada2f694967ab50a76"
sys.path.insert(0, str(CHECKOUT / "engine/Tools/Gltf/scripts"))
from run_editor_tests import CliSession, bounded, check_status, decoded, digest, discover, require, save, tool_value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("discover", "repro"))
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--confirmed-discovery", type=Path)
    parser.add_argument("--confirmed-sha256")
    args = parser.parse_args()
    project = args.project.resolve(strict=True)
    output = args.output.resolve()
    require(args.pid > 0 and project.suffix == ".sbproj", "Expected existing editor PID and project")
    fixture = OWNED / "SimpleInstancingCrashRepro.csx"
    request_file = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/SimpleInstancing') / "crash-repro-request.json"
    source, raw_request = bounded(fixture, 1024 * 1024), bounded(request_file, 16384)
    require(digest(source) == FIXTURE_SHA256 and digest(raw_request) == REQUEST_SHA256,
            "Frozen fixture or request changed; root must review the new source before dispatch")
    request = decoded(raw_request)
    require(request["id"] == "SimpleInstancing/glTF" and request["timeoutSeconds"] == 90,
            "Expected the frozen one-case request")
    approved = None
    if args.mode == "repro":
        require(args.confirmed_discovery is not None and args.confirmed_sha256,
                "Repro requires reviewed discovery and its exact SHA-256")
        approved_raw = bounded(args.confirmed_discovery, 1024 * 1024)
        require(digest(approved_raw) == args.confirmed_sha256, "Reviewed discovery hash differs")
        approved = decoded(approved_raw)
        require(approved.get("mode") == "discover" and approved.get("tests_dispatched") is False
                and approved.get("pid") == args.pid and approved.get("project") == str(project),
                "Reviewed discovery belongs to another editor/project")
        require(Path(request["outputDir"]).resolve() == output / "case",
                "Use the frozen request's fresh output parent")
    output.mkdir(parents=True, exist_ok=False)
    inputs = {"fixture_sha256": digest(source), "request_sha256": digest(raw_request),
              "runner_sha256": digest(Path(__file__).read_bytes()),
              "transport_sha256": digest((CHECKOUT / "engine/Tools/Gltf/scripts/run_editor_tests.py").read_bytes())}
    save(output / "inputs.json", inputs)
    summary = {"mode": args.mode, "tests_dispatched": False, "transport_completed": False,
               "scope": "One isolated public SimpleInstancing native capture, diagnostic phase and callback owner trace"}
    session = None
    exit_code = 1
    try:
        session = CliSession(CHECKOUT / "game/bin/managed/sbox-cli", args.pid, output, 110)
        live, exchange = discover(session, args.pid, project, inputs, output)
        if args.mode == "discover":
            summary.update(transport_completed=True, discovery_sha256=digest((output / "discovery.json").read_bytes()))
            exit_code = 0
        else:
            require(live["tools"] == approved["tools"], "Live schemas changed; root must review discovery")
            require(digest(fixture.read_bytes()) == inputs["fixture_sha256"], "Fixture changed before dispatch")
            script_path = exchange / "simple_instancing_crash_repro.csx"
            request_path = exchange / "gltf-run-case.json"
            with script_path.open("xb") as stream:
                stream.write(source)
            with request_path.open("xb") as stream:
                stream.write(raw_request)
            require(script_path.read_bytes() == source and request_path.read_bytes() == raw_request,
                    "Session source/request copy differs")
            save(output / "session-inputs.json", {"script": str(script_path), "request": str(request_path)})
            summary["tests_dispatched"] = True
            result = tool_value(session.call(live["tools"]["run_csharp"], {"path": str(script_path)}))
            save(output / "editor-result.json", result)
            summary["transport_completed"] = True
            summary["result"] = result
            require(result.get("id") == request["id"], "Returned another case")
            require(result.get("load", {}).get("status") == "passed"
                    and result.get("render", {}).get("status") == "passed"
                    and result.get("cleanupConfirmed") is True
                    and result.get("cleanup_errors") == []
                    and result.get("crashTraceErrors") == [], "Isolated case failed; inspect exact result/phase trace")
            status = tool_value(session.call(live["tools"]["editor_status"], {}))
            check_status(status, project)
            save(output / "status-after.json", status)
            exit_code = 0
    except Exception as error:
        summary["error"] = f"{type(error).__name__}: {error}"
    finally:
        if session is not None:
            summary["disconnect"] = session.close()  # cooperative disconnect only; never kill or relaunch
        save(output / "summary.json", summary)
    print(json.dumps({"output": str(output), "transport_completed": summary["transport_completed"],
                      "tests_dispatched": summary["tests_dispatched"], "error": summary.get("error")}))
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
