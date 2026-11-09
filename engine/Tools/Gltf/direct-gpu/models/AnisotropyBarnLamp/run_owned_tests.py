#!/usr/bin/env python3
"""Invoke under the shared native flock and host resource guard."""
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

from audit_source import MODEL, ROOT, inspect


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_inputs(audit):
    pin = subprocess.check_output(["git", "-C", str(MODEL.parent.parent), "rev-parse", "HEAD"], text=True).strip()
    assert pin == audit["corpusCommit"], "Upstream corpus revision changed."
    for expected in audit["variants"]:
        assert inspect(MODEL / expected["variant"] / expected["file"]) == expected, "Pinned source stream/image changed."


owned = Path(__file__).resolve().parent
test_root = ROOT / "Temp/gltf-validation/direct-gpu"
request, output = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/AnisotropyBarnLamp') / sys.argv[1], test_root / sys.argv[2]
ready = json.loads((test_root.parent / "editor-session-ready.json").read_text())
audit = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/AnisotropyBarnLamp') / "source-audit.json").read_text())
verify_inputs(audit)
assert ready["ready"] and not output.exists(), "Expected a fresh output and existing ready editor."
host = json.loads(subprocess.check_output([str(Path.home() / ".local/bin/codex-resource-guard"), "status"]))
editor = next(row for row in host["editors"] if row["pid"] == ready["pid"])
assert editor["start"] == ready["start"] and Path(editor["project"]).resolve() == Path(ready["project"]).resolve()
assert Path("/proc/sys/kernel/random/boot_id").read_text().strip() == ready["bootId"]
for module in ready["loaded"]["modules"]:
    assert module["loadedMvid"] == module["diskMvid"] and digest(Path(module["path"])) == module["sha256"], module["name"]
status = json.loads(subprocess.check_output([
    str(ROOT / "game/bin/managed/sbox-cli"), "--connect", str(editor["pid"]), "--call", "editor_status"
], text=True))
assert Path(status["Paths"]["ProjectRoot"]).resolve() == Path(ready["project"]).resolve().parent
assert not (status["IsPlaying"] or status["IsCompiling"] or status["SceneHasUnsavedChanges"]) and status["LastCompileSucceeded"]
shutil.copyfile(request, test_root / "current-tests.json")
code = subprocess.call([
    sys.executable, str(ROOT / "engine/Tools/Gltf/scripts/run_editor_probes.py"), "editor_direct_gpu_tests.csx",
    "--pid", str(editor["pid"]), "--project", ready["project"], "--output", str(output), "--timeout", "600",
    "--confirmed-discovery", ready["discoveryPath"], "--confirmed-sha256", ready["discoverySha256"]
], cwd=ROOT)
verify_inputs(audit)
if output.exists():
    shutil.copyfile((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/AnisotropyBarnLamp') / "source-audit.json", output / "model-source-audit.json")
    (output / "producer-session-ready.json").write_text(json.dumps(ready, indent=2) + "\n")
    for path in output.rglob("receipt-*.json"):
        value = json.loads(path.read_text())
        if "expected_cases" not in value:
            continue
        print(json.dumps({"receipt": str(path), "sha256": digest(path), **{
            key: value.get(key) for key in ("passed", "expected_cases", "passed_cases", "failed_cases", "incomplete_cases", "error")
        }}))
        if not value["passed"]:
            code = 1
raise SystemExit(code)
