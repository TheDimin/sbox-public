#!/usr/bin/env python3
"""Discover an existing editor, then run the frozen glTF suite after schema review.

Root must guard this entire foreground process and verify the physical desktop
window first. This program only attaches; it never launches or kills an editor.
Discovery performs read-only MCP calls, saves their full replies and exits.
Run mode requires the reviewed discovery artifact's explicit SHA-256, repeats
discovery, and refuses changed tool schemas or frozen inputs before run_csharp.
"""
import argparse
from collections import Counter
import hashlib
import json
import math
import queue
import re
import subprocess
import sys
import threading
import time
import uuid
from pathlib import Path

PROTOCOL = "2025-11-25"
MAX_RESPONSE = 64 * 1024 * 1024
MAX_STDERR = 8 * 1024 * 1024
EXPECTED_TOTALS = (44, 432, 432, 764)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True,
                      separators=(",", ":"), allow_nan=False).encode("utf-8")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON property: {key}")
        result[key] = value
    return result


def decoded(data):
    return json.loads(data.decode("utf-8"), object_pairs_hook=unique_object,
                      parse_constant=lambda value: require(False, f"Invalid JSON number: {value}"))


def bounded(path, maximum=MAX_RESPONSE):
    with path.open("rb") as stream:
        data = stream.read(maximum + 1)
    require(0 < len(data) <= maximum, f"Empty or oversized file: {path}")
    return data


def save(path, value):
    path.write_bytes(encoded(value) + b"\n")


def field(value, name):
    require(isinstance(value, dict), f"Expected an object containing {name}")
    matches = [item for key, item in value.items() if key.casefold() == name.casefold()]
    require(len(matches) == 1, f"Missing or ambiguous result property: {name}")
    return matches[0]


def tool_value(result):
    require(isinstance(result, dict) and result.get("isError") is not True,
            "Tool failed; inspect retained full reply")
    if "structuredContent" in result:
        return result["structuredContent"]
    content = result.get("content", [])
    require(len(content) == 1 and content[0].get("type") == "text",
            "Expected one JSON text block or structuredContent")
    return decoded(content[0]["text"].encode("utf-8"))


def find_tool(value, name):
    matches = [tool for tool in value.get("tools", []) if tool.get("name") == name]
    require(len(matches) == 1, f"Expected exactly one live schema for {name}")
    return matches[0]


def validate_arguments(tool, arguments):
    schema = tool.get("inputSchema", {})
    require(schema.get("type") == "object", f"Unsupported input schema: {tool['name']}")
    properties = schema.get("properties", {})
    require(set(arguments) <= set(properties), f"Unverified arguments for {tool['name']}")
    require(set(schema.get("required", [])) <= set(arguments),
            f"Missing required arguments for {tool['name']}")
    for name, value in arguments.items():
        kind = properties[name].get("type")
        require(kind == "string" and isinstance(value, str),
                f"Expected an advertised string argument: {tool['name']}.{name}")


class CliSession:
    """One outstanding request; bounded stdout reader and independent stderr drain."""

    def __init__(self, cli, pid, output, timeout):
        self.output, self.timeout = output, timeout
        self.events = queue.Queue(maxsize=2)
        self.pending = None
        self.next_id = self.incoming = 0
        self.stderr_bytes = 0
        self.process = subprocess.Popen(
            [str(cli), "--connect", str(pid), "--request-timeout", str(timeout),
             "--idle-timeout", "120"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.readers = [threading.Thread(target=self._stdout, daemon=True),
                        threading.Thread(target=self._stderr, daemon=True)]
        for reader in self.readers:
            reader.start()

    def _stdout(self):
        try:
            while True:
                line = self.process.stdout.readline(MAX_RESPONSE + 1)
                if not line:
                    self.events.put(("error", "CLI stdout closed"))
                    return
                if len(line) > MAX_RESPONSE or not line.endswith(b"\n"):
                    (self.output / "invalid-stdout.bin").write_bytes(line)
                    self.events.put(("error", "Oversized or unterminated CLI response"))
                    return
                self.events.put(("line", line))
        except (OSError, ValueError) as error:
            self.events.put(("error", str(error)))

    def _stderr(self):
        with (self.output / "cli-stderr.log").open("wb") as log:
            while chunk := self.process.stderr.read1(65536):
                remaining = max(0, MAX_STDERR - self.stderr_bytes)
                log.write(chunk[:remaining])
                log.flush()
                self.stderr_bytes += len(chunk)

    def send(self, value):
        data = encoded(value) + b"\n"
        require(len(data) <= 8 * 1024 * 1024, "Request exceeds CLI transport limit")
        self.process.stdin.write(data)
        self.process.stdin.flush()

    def request(self, method, params=None):
        require(self.pending is None, "A CLI request is already outstanding")
        self.next_id += 1
        self.pending = self.next_id
        message = {"jsonrpc": "2.0", "id": self.pending, "method": method}
        if params is not None:
            message["params"] = params
        save(self.output / f"request-{self.pending:03}.json", message)
        self.send(message)
        deadline = time.monotonic() + self.timeout + 30
        for _ in range(129):
            remaining = deadline - time.monotonic()
            require(remaining > 0, "CLI response deadline exceeded")
            try:
                kind, data = self.events.get(timeout=remaining)
            except queue.Empty as error:
                raise TimeoutError("CLI response deadline exceeded") from error
            if kind != "line":
                raise ValueError(f"CLI transport error: {data}")
            self.incoming += 1
            (self.output / f"reply-{self.incoming:03}.json").write_bytes(data)
            value = decoded(data)
            require(isinstance(value, dict) and value.get("jsonrpc") == "2.0",
                    "Invalid JSON-RPC response envelope")
            if "error" in value:
                if value.get("id") == self.pending:
                    self.pending = None
                raise ValueError(f"JSON-RPC failure: {value['error']}")
            if "id" not in value:
                continue
            require(value["id"] == self.pending, "Unexpected JSON-RPC response ID")
            self.pending = None
            require("result" in value, "JSON-RPC response has no result")
            return value["result"]
        raise ValueError("Too many unsolicited protocol messages")

    def call(self, tool, arguments):
        validate_arguments(tool, arguments)
        result = self.request("tools/call", {"name": tool["name"], "arguments": arguments})
        require(isinstance(result, dict) and result.get("isError") is not True,
                f"{tool['name']} reported an error; inspect full reply")
        return result

    def close(self):
        # Normal exit occurs only after the final reply. A failed transport is
        # cooperatively disconnected and explicitly recorded as incomplete.
        pending = self.pending
        try:
            self.process.stdin.close()
        except (OSError, BrokenPipeError):
            pass
        try:
            code = self.process.wait(timeout=30)
        except subprocess.TimeoutExpired:
            # Do not kill, detach, or return while the guarded child is live.
            print("Attached CLI has not exited; waiting without killing the editor.",
                  file=sys.stderr, flush=True)
            code = self.process.wait()
        for reader in self.readers:
            reader.join(timeout=2)
        return {"exit_code": code, "interrupted_request": pending,
                "stderr_bytes": self.stderr_bytes,
                "stderr_truncated": self.stderr_bytes > MAX_STDERR}


def snapshot(checkout, cli):
    base = checkout / "research/gltf-validation"
    manifest_bytes = bounded(base / "gltf-test-manifest.json", 1024 * 1024)
    manifest = decoded(manifest_bytes)
    require(manifest.get("version") == 7, "Expected frozen manifest version 7")
    require(tuple(manifest.get(key) for key in ("source_class_count", "test_method_count",
            "data_row_count", "expanded_case_count")) == EXPECTED_TOTALS,
            "Frozen manifest totals changed; review the runner before execution")
    test_root = checkout / "engine/Tests/Sandbox.Test.Gltf"
    listed = {suite["file"] for suite in manifest["suites"]}
    require(listed == {path.name for path in test_root.glob("*.cs")},
            "Manifest source inventory differs from disk")
    resources = {name for item in manifest["suites"] + manifest["codec_preflights"]
                 for name in item["resources"]}
    def hashes(names):
        result = {}
        for name in sorted(names):
            path = (test_root / name).resolve(strict=True)
            require(path.is_relative_to(test_root.resolve()), "Manifest input escapes test root")
            result[name] = digest(bounded(path, 1024 * 1024))
        return result
    return manifest, {
        "manifest_sha256": digest(manifest_bytes),
        "runner_source_sha256": digest(bounded(checkout / "engine/Tools/Gltf/editor/editor_gltf_unit_tests.csx", 1024 * 1024)),
        "test_source_sha256": hashes(listed), "resource_sha256": hashes(resources),
        "framework_sha256": digest(bounded(test_root / "bin/Release/net10.0/Microsoft.VisualStudio.TestPlatform.TestFramework.dll")),
        "gltf_binary_sha256": digest(bounded(checkout / "game/bin/managed/Sandbox.Gltf.dll")),
        "engine_binary_sha256": digest(bounded(checkout / "game/bin/managed/Sandbox.Engine.dll")),
        "renderer_binary_sha256": digest(bounded(checkout / "game/bin/managed/Sandbox.SceneRenderer.dll")),
        "cli_sha256": digest(bounded(cli)),
        "orchestrator_sha256": digest(bounded(Path(__file__).resolve(), 1024 * 1024)),
    }


def check_status(status, project):
    require(Path(field(field(status, "Paths"), "ProjectRoot")).resolve() == project.parent,
            "editor_status project directory differs from the requested project")
    require(field(status, "IsPlaying") is False, "Exit play mode before the pure suite")
    require(field(status, "IsCompiling") is False, "Editor compiler is still active")
    require(field(status, "LastCompileSucceeded") is not False and
            field(status, "LastCompileErrors") == 0, "Editor reports compilation errors")


def discover(session, pid, project, inputs, output):
    initialized = session.request("initialize", {
        "protocolVersion": PROTOCOL, "capabilities": {},
        "clientInfo": {"name": "gltf-editor-tests", "version": "1"}})
    require(initialized.get("protocolVersion") == PROTOCOL, "Protocol version was not accepted")
    meta = initialized.get("_meta", {})
    require(meta.get("pid") == pid and meta.get("kind") == "editor", "Unexpected editor PID/kind")
    observed_project = Path(meta.get("project", ""))
    require(observed_project.is_absolute() and observed_project.resolve() == project,
            "Unexpected editor project")
    exchange = Path(meta.get("exchangeDirectory", ""))
    require(exchange.is_absolute() and exchange.is_dir(), "Missing actual session exchange directory")
    instructions = initialized.get("instructions")
    require(isinstance(instructions, str) and instructions.strip(), "Missing session instructions")
    (output / "instructions.txt").write_text(instructions, encoding="utf-8")
    session.send({"jsonrpc": "2.0", "method": "notifications/initialized"})
    listed = session.request("tools/list")
    tools = {name: find_tool(listed, name) for name in ("editor_status", "search_tools", "read_console")}
    status = tool_value(session.call(tools["editor_status"], {}))
    check_status(status, project)
    found = tool_value(session.call(tools["search_tools"], {"query": "run_csharp"}))
    tools["run_csharp"] = find_tool(found, "run_csharp")
    validate_arguments(tools["run_csharp"], {"path": str(exchange / "editor_gltf_unit_tests.csx")})
    save(output / "schemas.json", tools)
    value = {"version": 1, "mode": "discover", "tests_dispatched": False,
             "pid": pid, "project": str(project), "metadata": meta,
             "editor_status": status, "tools": tools, "inputs": inputs,
             "instructions_sha256": digest(instructions.encode("utf-8"))}
    save(output / "discovery.json", value)
    return value, exchange


def receipt_outcome(receipt):
    require(isinstance(receipt, dict), "Runner receipt must be a JSON object")
    outcome = {key: receipt.get(key) for key in (
        "phase", "expected_cases", "passed_cases", "failed_cases", "blocked_cases", "partial_cases")}
    error = receipt.get("error")
    outcome["error_excerpt"] = str(error).encode("utf-8")[:500].decode("utf-8", errors="ignore") if error is not None else None
    outcome["diagnostic_count"] = len(receipt.get("diagnostics", []))
    outcome["cleanup_error_count"] = len(receipt.get("cleanup_errors", []))
    return outcome


def verify_receipt(receipt, request, request_path, request_hash, inputs, manifest):
    # Compilation/setup failures legitimately lack helper/framework identities.
    # Report the producer's evidence before checking fields that only exist after
    # those phases succeed. This remains a failed run, never a partial pass.
    outcome = receipt_outcome(receipt)
    require(receipt.get("passed") is True and receipt.get("all_project_cases_passed") is True and
            receipt.get("phase") == "complete" and receipt.get("error") is None and
            receipt.get("cleanup_errors") == [],
            f"Runner reported an unsuccessful outcome in phase {outcome['phase']}; inspect retained editor-receipt.json")
    require(receipt.get("request_values") == request and receipt.get("request_id") == request["request_id"],
            "Receipt request values or ID differ")
    require(receipt.get("request_path") == str(request_path) and receipt.get("request_sha256") == request_hash,
            "Receipt request path/hash differ")
    require(receipt.get("selection") == "all" and receipt.get("selection_source") == "request",
            "Receipt did not select all cases through the session request")
    for key in ("manifest_sha256", "runner_source_sha256", "test_source_sha256",
                "resource_sha256", "framework_sha256", "gltf_binary_sha256"):
        require(receipt.get(key) == inputs[key], f"Receipt input identity differs: {key}")
    for key in ("gltf_loaded_mvid", "gltf_disk_mvid", "engine_mvid", "helper_mvid",
                "framework_expected_mvid", "framework_loaded_mvid"):
        value = receipt.get(key)
        require(isinstance(value, str) and value, f"Missing module identity: {key}")
        try:
            identity = uuid.UUID(value)
        except ValueError as error:
            raise ValueError(f"Invalid module identity: {key}") from error
        require(identity.int != 0, f"Empty module identity: {key}")
    require(receipt["gltf_loaded_mvid"] == receipt["gltf_disk_mvid"], "Loaded/disk glTF MVID differs")
    require(receipt["framework_expected_mvid"] == receipt["framework_loaded_mvid"] and
            receipt.get("framework_loaded_sha256") == inputs["framework_sha256"],
            "Resolved MSTest framework identity differs")
    require(receipt.get("is_editor") is True and receipt.get("manifest_version") == 7,
            "Receipt does not identify the expected editor/manifest")
    tests = receipt.get("tests", [])
    require(len(tests) == EXPECTED_TOTALS[3], "Receipt has an incomplete case inventory")
    require(Counter(test["suite"] for test in tests) ==
            {suite["name"]: suite["cases"] for suite in manifest["suites"]},
            "Receipt suite counts differ from manifest")
    preflights = receipt.get("preflights", [])
    require(len(preflights) == 3 and {item["codec"] for item in preflights} == {"draco", "meshopt", "ktx"},
            "Missing or duplicate positive native codec preflights")
    expected = {item["name"]: item for item in manifest["codec_preflights"]}
    for item in preflights:
        spec = expected[item["codec"]]
        require(item.get("test") == spec["type"] + "." + spec["method"] and
                item.get("arguments") == spec["arguments"] and item.get("passed") is True,
                f"Native codec preflight failed or differs: {item['codec']}")
    for item in tests + preflights:
        require(item.get("before_frame") is not None and item.get("before_frame") == item.get("after_frame"),
                "A synchronous case/preflight advanced frames or never executed")
        require(len(item.get("arguments", [])) == len(item.get("argument_types", [])),
                "Missing exact argument type evidence")
        for value, clr in zip(item.get("arguments", []), item.get("argument_types", [])):
            valid = ((clr == "System.String" and type(value) is str) or
                     (clr == "System.Boolean" and type(value) is bool) or
                     (clr == "System.Int32" and type(value) is int and -(2 ** 31) <= value < 2 ** 31) or
                     (clr == "System.UInt32" and type(value) is int and 0 <= value < 2 ** 32) or
                     (clr == "System.Byte" and type(value) is int and 0 <= value < 256) or
                     (clr == "System.Single" and type(value) in (int, float) and math.isfinite(value) and abs(value) <= 3.4028234663852886e38))
            require(valid, "Argument value disagrees with its supported CLR type")
    require(all(item.get("status") == "passed" for item in tests), "One or more project cases failed/blocked")
    require(receipt.get("expected_cases") == 764 and receipt.get("passed_cases") == 764 and
            all(receipt.get(key) == 0 for key in ("failed_cases", "blocked_cases", "partial_cases")),
            "Runner case totals do not establish a complete 764-case pass")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("discover", "run"))
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--checkout", type=Path, default=Path(__file__).resolve().parents[4])
    parser.add_argument("--cli", type=Path)
    parser.add_argument("--output", type=Path, required=True, help="New artifact directory; must not already exist")
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--confirmed-discovery", type=Path)
    parser.add_argument("--confirmed-sha256")
    args = parser.parse_args(argv)
    session = None
    live = None
    console_read = False
    created_output = False
    summary = {"mode": args.mode, "tests_dispatched": False, "passed": False,
               "scope": "764 frozen pure-project cases; excludes full corpus, GPU and editor-feature conformance"}
    output = args.output.resolve()
    try:
        require(args.pid > 0 and 1 <= args.timeout <= 86400, "Invalid PID or timeout")
        checkout, project = args.checkout.resolve(strict=True), args.project.resolve(strict=True)
        require(project.is_file() and project.suffix == ".sbproj", "Expected an existing .sbproj file")
        cli = (args.cli or checkout / "game/bin/managed/sbox-cli").resolve(strict=True)
        require(cli.is_file(), "Installed CLI is not a file")
        output.mkdir(parents=True, exist_ok=False)
        created_output = True
        manifest, inputs = snapshot(checkout, cli)
        confirmed = None
        if args.mode == "run":
            require(args.confirmed_discovery and re.fullmatch(r"[0-9a-f]{64}", args.confirmed_sha256 or ""),
                    "Run requires a reviewed discovery artifact and its explicit SHA-256")
            raw = bounded(args.confirmed_discovery)
            require(digest(raw) == args.confirmed_sha256, "Confirmed discovery hash differs")
            confirmed = decoded(raw)
            require(confirmed.get("version") == 1 and confirmed.get("mode") == "discover" and
                    confirmed.get("tests_dispatched") is False and confirmed.get("pid") == args.pid and
                    confirmed.get("project") == str(project) and confirmed.get("inputs") == inputs,
                    "Confirmed discovery identity or frozen source/binary inputs changed")
        session = CliSession(cli, args.pid, output, args.timeout)
        live, exchange = discover(session, args.pid, project, inputs, output)
        summary["discovery_path"] = str(output / "discovery.json")
        summary["discovery_sha256"] = digest(bounded(output / "discovery.json"))
        if args.mode == "run":
            require(live["tools"] == confirmed["tools"], "Live tool schemas changed; review discovery again")
            request = {"version": 1, "request_id": uuid.uuid4().hex, "suite": "all",
                       "manifest_sha256": inputs["manifest_sha256"]}
            request_bytes = encoded(request)
            request_path = exchange / "gltf-unit-request.json"
            runner = bounded(checkout / "engine/Tools/Gltf/editor/editor_gltf_unit_tests.csx", 1024 * 1024)
            require(digest(runner) == inputs["runner_source_sha256"], "Runner changed after discovery")
            runner_path = exchange / "editor_gltf_unit_tests.csx"
            for path, data in ((request_path, request_bytes), (runner_path, runner)):
                with path.open("xb") as stream:
                    stream.write(data)
                require(bounded(path) == data, f"Session input copy differs: {path}")
            save(output / "run-inputs.json", {"inputs": inputs, "request": request,
                 "request_path": str(request_path), "request_sha256": digest(request_bytes),
                 "runner_path": str(runner_path)})
            summary["tests_dispatched"] = True
            returned = tool_value(session.call(live["tools"]["run_csharp"], {"path": str(runner_path)}))
            save(output / "runner-result.json", returned)
            receipt_path = Path(returned["receipt_path"]).resolve(strict=True)
            receipt_root = (checkout / "Temp/gltf-validation/editor-gltf-unit-tests").resolve()
            require(receipt_path.is_relative_to(receipt_root) and receipt_path.name == "receipt.json",
                    "Runner returned a receipt outside its expected output tree")
            receipt_bytes = bounded(receipt_path)
            (output / "editor-receipt.json").write_bytes(receipt_bytes)
            receipt = decoded(receipt_bytes)
            summary.update(receipt_path=str(receipt_path), receipt_sha256=digest(receipt_bytes),
                           runner_outcome=receipt_outcome(receipt))
            # Console text is deliberately retained without trying to parse it as JSON.
            session.call(live["tools"]["read_console"], {})
            console_read = True
            status_after = tool_value(session.call(live["tools"]["editor_status"], {}))
            check_status(status_after, project)
            for name in ("ActiveScene", "ActiveScenePath", "SceneHasUnsavedChanges", "IsPlaying"):
                require(field(status_after, name) == field(live["editor_status"], name),
                        f"Editor state changed during pure suite: {name}")
            require(snapshot(checkout, cli)[1] == inputs, "Frozen inputs changed during execution")
            verify_receipt(receipt, request, request_path, digest(request_bytes), inputs, manifest)
            summary.update(passed=True, expected_cases=764, receipt_path=str(receipt_path),
                           receipt_sha256=digest(receipt_bytes))
        else:
            summary["discovery_complete"] = True
    except (OSError, ValueError, KeyError, TypeError, TimeoutError, KeyboardInterrupt) as error:
        summary["error"] = f"{type(error).__name__}: {error}"
    finally:
        if session is not None:
            if (args.mode == "run" and summary["tests_dispatched"] and live is not None
                    and session.pending is None and not console_read):
                try:
                    session.call(live["tools"]["read_console"], {})
                except (OSError, ValueError, TimeoutError) as error:
                    summary["console_error"] = f"{type(error).__name__}: {error}"
            summary["cli_exit"] = session.close()
            if summary["cli_exit"]["exit_code"] != 0 or summary["cli_exit"]["interrupted_request"] is not None:
                summary["passed"] = False
                summary["error"] = summary.get("error", "CLI did not finish cleanly")
        if created_output:
            save(output / "summary.json", summary)
        print(json.dumps(summary, indent=2), flush=True)
    return 0 if summary.get("passed") or (summary.get("discovery_complete") and "error" not in summary) else 1


if __name__ == "__main__":
    sys.exit(main())
