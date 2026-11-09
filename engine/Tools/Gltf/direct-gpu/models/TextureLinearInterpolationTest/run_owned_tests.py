#!/usr/bin/env python3
"""Execute only within the shared native flock and admitted host resource guard."""
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[6]
here = Path(__file__).resolve().parent
label = sys.argv[1]
assert label.isascii() and label.replace('-', '').isalnum()
output = root / 'Temp/gltf-validation/direct-gpu/TextureLinearInterpolationTest/runs' / label
assert not output.exists(), 'Use a fresh run label'
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
ready = json.loads((root / 'Temp/gltf-validation/editor-session-ready.json').read_text())
host = json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard', 'status']))
editor = next(x for x in host['editors'] if x['pid'] == ready['pid'])
assert ready['ready'] and editor['start'] == ready['start'] and Path(editor['project']).resolve() == Path(ready['project']).resolve()
status = json.loads(subprocess.check_output([str(root / 'game/bin/managed/sbox-cli'), '--connect', str(ready['pid']), '--call', 'editor_status'], cwd=root, timeout=40))
# sbox-cli may print structured content directly, as observed in its live schema.
assert not status['IsPlaying'] and not status['IsCompiling'] and status['LastCompileSucceeded']
assert Path(status['Paths']['ProjectRoot']).resolve() == Path(ready['project']).parent.resolve()
audit = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/TextureLinearInterpolationTest') / 'source-audit.json').read_text())
def verify():
    for f in audit['files']:
        assert digest(root / f['path']) == f['sha256'], f['path']
verify()
modules = {x['name']: x for x in ready['loaded']['modules']}
for x in modules.values():
    assert x['loadedMvid'] == x['diskMvid'] and digest(Path(x['path'])) == x['sha256']
request_path = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/TextureLinearInterpolationTest') / 'request.json'
request = json.loads(request_path.read_text())
assert request['engine_sha256'] == modules['Sandbox.Engine']['sha256']
assert request['gltf_sha256'] == modules['Sandbox.Gltf']['sha256']
assert request['framework_sha256'] == digest(root / 'engine/Tests/Sandbox.Test.Gltf/bin/Release/net10.0/Microsoft.VisualStudio.TestPlatform.TestFramework.dll')
for source in request['sources']:
    assert digest(root / source['path']) == source['sha256'], 'Frozen helper changed: ' + source['path']
output.mkdir(parents=True)
(output / 'request.json').write_text(json.dumps(request,indent=2)+'\n')
(output / 'session.json').write_text(json.dumps(ready,indent=2)+'\n')
(output / 'host.json').write_text(json.dumps(host,indent=2)+'\n')
(output / 'status.json').write_text(json.dumps(status,indent=2)+'\n')
shader_hash = digest(here / 'linear_probe.shader')
assert shader_hash == json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/TextureLinearInterpolationTest') / 'submission.json').read_text())['additional_frozen_input']['sha256']
shutil.copyfile(here / 'linear_probe.shader', output / 'linear_probe.shader')
shutil.copyfile(output / 'request.json', root / 'Temp/gltf-validation/direct-gpu/current-tests.json')
code = subprocess.call([sys.executable, str(root / 'engine/Tools/Gltf/scripts/run_editor_probes.py'), 'editor_direct_gpu_tests.csx', '--pid', str(ready['pid']), '--project', ready['project'], '--output', str(output / 'probe'), '--timeout', '1200', '--confirmed-discovery', ready['discoveryPath'], '--confirmed-sha256', ready['discoverySha256']],cwd=root)
verify()
assert digest(here / 'linear_probe.shader') == shader_hash
receipts = list(output.rglob('receipt-*.json'))
assert len(receipts)==1, 'Missing receipt'
receipt=json.loads(receipts[0].read_text())
print(json.dumps(dict(receipt=str(receipts[0]), sha256=digest(receipts[0]), passed=receipt.get('passed'), phase=receipt.get('phase'), error=receipt.get('error'), rows=receipt.get('rows'), diagnostics=receipt.get('diagnostics')),indent=2))
raise SystemExit(code or (0 if receipt.get('passed') else 1))
