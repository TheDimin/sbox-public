#!/usr/bin/env python3
"""Dispatch the frozen FlightHelmet suite inside the shared lock and guard."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parents[6]
owned = Path(__file__).resolve().parent
validation = root / "Temp/gltf-validation"
corpus = root / "Temp/gltf-reference/glTF-Sample-Assets"
cli = root / "game/bin/managed/sbox-cli"
guard = Path.home() / ".local/bin/codex-resource-guard"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def verify_inputs(audit):
    revision = subprocess.check_output(["git", "-C", corpus, "rev-parse", "HEAD"], text=True).strip()
    require(revision == audit["pin"], "FlightHelmet reference corpus revision changed")
    for file in audit["files"]:
        path = corpus / "Models/FlightHelmet" / file["path"]
        require(digest(path) == file["sha256"], f"Pinned source changed: {path}")


def main(request, output):
    audit = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/FlightHelmet') / "source-audit.json").read_bytes())
    verify_inputs(audit)
    marker = json.loads((validation / "editor-session-ready.json").read_bytes())
    require(marker["ready"], "Editor readiness marker is inactive")
    project = Path(marker["project"]).resolve()
    host = json.loads(subprocess.check_output([guard, "status"], text=True))
    editor = next(row for row in host["editors"] if row["pid"] == marker["pid"])
    require(str(editor["start"]) == str(marker["start"]) and Path(editor["project"]).resolve() == project,
            "Host editor identity changed")
    boot = Path("/proc/sys/kernel/random/boot_id").read_text().strip()
    require(boot == marker["bootId"], "Editor marker names a previous boot")
    status = json.loads(subprocess.check_output([cli, "--connect", str(editor["pid"]), "--call", "editor_status"], text=True))
    require(Path(status["Paths"]["ProjectRoot"]).resolve() == project.parent and
            not status["IsPlaying"] and not status["IsCompiling"] and not status["SceneHasUnsavedChanges"] and
            status["LastCompileSucceeded"], "Editor is not idle with the expected clean project")
    for module in marker["loaded"]["modules"]:
        require(module["loadedMvid"] == module["diskMvid"] and digest(Path(module["path"])) == module["sha256"],
                f"Producer changed: {module['name']}")
    require(not output.exists(), "Refusing to replace an existing validation receipt")
    shutil.copyfile(request, validation / "direct-gpu/current-tests.json")
    result = subprocess.run([
        sys.executable, root / "engine/Tools/Gltf/scripts/run_editor_probes.py", "editor_direct_gpu_tests.csx",
        "--pid", str(editor["pid"]), "--project", project, "--output", output, "--timeout", "600",
        "--confirmed-discovery", marker["discoveryPath"], "--confirmed-sha256", marker["discoverySha256"],
    ], cwd=root)
    verify_inputs(audit)
    if not output.is_dir():
        raise RuntimeError(f"Runner returned {result.returncode} before creating a receipt")
    (output / "source-audit.json").write_text(json.dumps(audit, indent=2) + "\n")
    (output / "verified-editor.json").write_text(json.dumps(marker, indent=2) + "\n")
    receipts = [(path, json.loads(path.read_bytes())) for path in output.rglob("receipt-*.json")]
    model_receipts = [(path, row) for path, row in receipts if "expected_cases" in row]
    for path, row in model_receipts:
        print(json.dumps({"receipt": str(path), "sha256": digest(path), **{
            key: row.get(key) for key in ("passed", "expected_cases", "passed_cases", "failed_cases",
                                          "incomplete_cases", "cleanup_errors", "error")
        }}), flush=True)
    return result.returncode or int(len(model_receipts) != 1 or not model_receipts[0][1]["passed"])


if __name__ == "__main__":
    raise SystemExit(main(Path(sys.argv[1]), Path(sys.argv[2])))
