#!/usr/bin/env python3
"""Run once under the shared flock and host resource guard; never launch an editor."""
import copy
import hashlib
import importlib.machinery
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[6]
BASE = ROOT / "research/gltf-validation"
OWNED = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "engine/Tools/Gltf/scripts"))
from run_editor_tests import CliSession, check_status, discover, save, tool_value


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    ready = json.loads((ROOT / "Temp/gltf-validation/editor-session-ready.json").read_text())
    require(ready["ready"] and ready["pid"] > 0 and
            ready["project"] == "/home/damian/projects/Survive/survive.sbproj", "Expected reviewed editor session for the saved project")
    plan_path = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/SimpleInstancing') / "review-camera-plan.json"
    plan = json.loads(plan_path.read_text())
    require([row["id"] for row in plan["cases"]] == ["SimpleInstancing/glTF"], "Original suffix order changed")
    output = Path(plan["output"])
    output.mkdir(parents=True, exist_ok=False)
    source = ROOT / "engine/Tools/Gltf/editor/editor_corpus.csx"
    require(sha(source) == plan["driver_sha256"], "Frozen public driver changed")
    require(sha(BASE / "reference-recipes.json") == plan["recipes_sha256"], "Recipes changed since suffix freeze")
    approved_bytes = Path(ready["discoveryPath"]).read_bytes()
    require(hashlib.sha256(approved_bytes).hexdigest() == ready["discoverySha256"], "Reviewed discovery changed")
    approved = json.loads(approved_bytes)
    expected_modules = {row["name"]: row for row in ready["loaded"]["modules"]}
    for row in expected_modules.values():
        require(sha(Path(row["path"])) == row["sha256"], "Installed module changed: " + row["name"])
    # Reuse only the established lossless APNG encoder, never Run's global
    # dataset/export/publication execution path.
    loader = importlib.machinery.SourceFileLoader("simple_instancing_suffix_support", str(ROOT / "engine/Tools/Gltf/scripts/Run"))
    module_spec = importlib.util.spec_from_loader(loader.name, loader)
    support = importlib.util.module_from_spec(module_spec)
    loader.exec_module(support)
    session = None
    summary = {"passed": False, "order": [], "results": [], "error": None,
               "scope": "Current source-material recipe capture; no global Run or visual approval"}
    try:
        session = CliSession(ROOT / "game/bin/managed/sbox-cli", ready["pid"], output, 150)
        live, exchange = discover(session, ready["pid"], Path(ready["project"]), {}, output)
        require(live["tools"] == approved["tools"], "Live tool schemas changed")
        script = exchange / "simple_instancing_original_suffix.csx"
        with script.open("xb") as stream:
            stream.write(source.read_bytes())
        save(output / "plan.json", plan)
        for row in plan["cases"]:
            for item in row["inputs"]:
                require(sha(Path(item["path"])) == item["sha256"], "Pinned source dependency changed")
            case_output = output / "cases" / row["id"]
            case_output.mkdir(parents=True, exist_ok=False)
            request = copy.deepcopy(row["request"])
            request["outputDir"] = str(case_output)
            save(case_output / "request.json", request)
            support.atomic_json(exchange / "gltf-run-case.json", request)
            require(sha(source) == plan["driver_sha256"], "Driver changed before dispatch")
            result = tool_value(session.call(live["tools"]["run_csharp"], {"path": str(script)}))
            save(case_output / "editor-result.json", result)
            summary["order"].append(row["id"])
            summary["results"].append(result)
            require(result.get("id") == row["id"], "Returned another capture case")
            require(result.get("load", {}).get("status") == "passed" and result.get("render", {}).get("status") == "passed", "Public capture failed")
            require(result.get("cleanupConfirmed") is True and result.get("cleanup_errors") == [], "Owned cleanup failed; stop before another case")
            observed = result["render"]["observation"]
            require(observed["callbackErrors"] == [] and observed["drawCalls"] > 0 and observed["requestedRevision"] == observed["submittedRevision"], "Current actual draw submission failed")
            if row["id"] == "SimpleInstancing/glTF":
                require(observed["bounds"]["MeshCount"] == 125 and observed["drawCalls"] >= 125, "Actual125-instance expansion was not submitted")
            for key, name in [("engine", "Sandbox.Engine"), ("gltf", "Sandbox.Gltf"), ("renderer", "Sandbox.SceneRenderer")]:
                producer = result["producer"][key]
                require(producer["mvid"] == producer["diskMvid"] == expected_modules[name]["loadedMvid"] and producer["diskSha256"] == expected_modules[name]["sha256"], "Loaded producer changed during suffix")
            encoded = copy.deepcopy(result)
            support.encode_motion(encoded, request["recipe"], case_output)
            save(case_output / "result.json", encoded)
            status = tool_value(session.call(live["tools"]["editor_status"], {}))
            check_status(status, Path(ready["project"]))
            print(json.dumps({"case": row["id"], "capture": encoded["capture"], "draws": observed["drawCalls"], "cleanup": True}), flush=True)
        save(output / "console-after.json", session.call(live["tools"]["read_console"], {}))
        summary["passed"] = True
    except Exception as error:
        summary["error"] = f"{type(error).__name__}: {error}"
    finally:
        if session is not None:
            summary["disconnect"] = session.close()
        save(output / "summary.json", summary)
    print(json.dumps({"passed": summary["passed"], "order": summary["order"], "error": summary["error"], "output": str(output)}), flush=True)
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
