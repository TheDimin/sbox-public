#!/usr/bin/env python3
"""Invoke only within the native flock and host resource guard."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parents[6]
request = Path(sys.argv[1]).resolve()
output = root / "Temp/gltf-validation/direct-gpu" / sys.argv[2]
ready = json.loads((root / "Temp/gltf-validation/editor-session-ready.json").read_text())
host = json.loads(subprocess.check_output(["/home/damian/.local/bin/codex-resource-guard", "status"]))
editor = next(item for item in host["editors"] if item["pid"] == ready["pid"])
assert editor["start"] == ready["start"] and Path(editor["project"]).resolve() == Path(ready["project"]).resolve()
subprocess.run([str(root / "game/bin/managed/sbox-cli"), "--connect", str(ready["pid"]), "--call", "editor_status"], check=True)
spec = json.loads(request.read_text())
for item in spec["sources"]:
    assert hashlib.sha256((root / item["path"]).read_bytes()).hexdigest() == item["sha256"], item["path"]
shutil.copyfile(request, root / "Temp/gltf-validation/direct-gpu/current-tests.json")
code = subprocess.call([sys.executable, str(root / "engine/Tools/Gltf/scripts/run_editor_probes.py"), "editor_direct_gpu_tests.csx", "--pid", str(ready["pid"]), "--project", ready["project"], "--output", str(output), "--timeout", "300", "--confirmed-discovery", ready["discoveryPath"], "--confirmed-sha256", ready["discoverySha256"]], cwd=root)
for path in output.rglob("receipt-*.json"):
    data = json.loads(path.read_text())
    if "expected_cases" in data:
        print(json.dumps({"receipt": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "passed": data["passed"], "passed_cases": data["passed_cases"], "failed_cases": data["failed_cases"], "cleanup_errors": data["cleanup_errors"]}), flush=True)
        if not data["passed"]:
            code = 1
raise SystemExit(code)
