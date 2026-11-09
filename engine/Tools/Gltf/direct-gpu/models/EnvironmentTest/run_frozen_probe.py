#!/usr/bin/env python3
"""Caller owns shared native flock and honest host guard through this runner."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[6]
OWNED = Path(__file__).resolve().parent
request = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/EnvironmentTest') / 'field-hdr-exposure-request.json'
selection_bytes = request.read_bytes()
selection = json.loads(selection_bytes)
sha = lambda data: hashlib.sha256(data).hexdigest()
ready = json.loads((ROOT / 'Temp/gltf-validation/editor-session-ready.json').read_text())
assert ready['ready'] is True
host = json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard', 'status']))
editor = next(row for row in host['editors'] if row['pid'] == ready['pid'])
assert editor['start'] == ready['start'] and Path(editor['project']).resolve() == Path(ready['project']).resolve(), 'Host editor identity changed'

def verify():
    assert request.read_bytes() == selection_bytes, 'Selection changed'
    for row in selection['sources'] + selection['frozen_artifacts']:
        assert sha((ROOT / row['path']).read_bytes()) == row['sha256'], 'Frozen artifact changed: ' + row['path']
    for key, name in [('engine_sha256', 'Sandbox.Engine.dll'), ('gltf_sha256', 'Sandbox.Gltf.dll'), ('tools_sha256', 'Sandbox.Tools.dll')]:
        assert sha((ROOT / 'game/bin/managed' / name).read_bytes()) == selection[key], 'Installed producer changed: ' + name

verify()
output = ROOT / 'Temp/gltf-validation/direct-gpu' / selection['runLabel']
assert not output.exists(), 'Use a fresh frozen run label; preserve prior receipts'
shutil.copyfile(request, ROOT / 'Temp/gltf-validation/direct-gpu/current-tests.json')
code = subprocess.call([sys.executable, str(ROOT / 'engine/Tools/Gltf/scripts/run_editor_probes.py'), 'editor_direct_gpu_tests.csx',
    '--pid', str(ready['pid']), '--project', ready['project'], '--output', str(output / 'transport'), '--timeout', '600',
    '--confirmed-discovery', ready['discoveryPath'], '--confirmed-sha256', ready['discoverySha256']], cwd=ROOT)
verify()
receipts = list((output / 'transport').rglob('receipt-*.json'))
inspected = 0
for path in receipts:
    value = json.loads(path.read_text())
    if 'expected_cases' not in value:
        continue
    inspected += 1
    assert value['request_sha256'] == sha(selection_bytes)
    assert value['expected_cases'] == sum(suite['expected_cases'] for suite in selection['suites'])
    print(json.dumps({'receipt': str(path), 'sha256': sha(path.read_bytes()), **{key: value.get(key) for key in ['passed', 'expected_cases', 'passed_cases', 'failed_cases', 'incomplete_cases', 'error', 'cleanup_errors']}}), flush=True)
    if not value.get('passed'):
        code = 1
assert inspected, 'No actual native receipt inspected inside the lease'
raise SystemExit(code)
