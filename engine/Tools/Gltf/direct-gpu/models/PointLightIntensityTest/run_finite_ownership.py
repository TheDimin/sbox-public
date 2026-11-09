#!/usr/bin/env python3
# Invoke inside the shared model lock and a host-guarded 2 GiB child.
import hashlib, json, shutil, subprocess, sys, uuid
from pathlib import Path
root=Path(__file__).resolve().parents[6]
here=Path(__file__).resolve().parent
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
assert ready['ready'] and ready['bootId']==Path('/proc/sys/kernel/random/boot_id').read_text().strip(), 'Missing ready marker for this host boot'
host=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
editor=next(e for e in host['editors'] if e['pid']==ready['pid'])
assert editor['start']==ready['start'] and Path(editor['project']).resolve()==Path(ready['project']).resolve()
status=subprocess.run([str(root/'game/bin/managed/sbox-cli'),'--connect',str(ready['pid']),'--call','editor_status'],cwd=root,text=True,capture_output=True,timeout=30)
print('live editor status:',status.stdout,status.stderr,flush=True)
assert status.returncode==0
live=json.loads(status.stdout)
assert Path(live['Paths']['ProjectRoot']).resolve()==Path(ready['project']).resolve().parent and not live['IsPlaying'] and not live['IsCompiling']
assert not live['SceneHasUnsavedChanges'] and live['LastCompileSucceeded'] and live['LastCompileErrors']==0
request=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/PointLightIntensityTest') / 'finite-ownership-request.json'; spec=json.loads(request.read_text())
for item in spec['sources']:
 assert hashlib.sha256((root/item['path']).read_bytes()).hexdigest()==item['sha256']
shutil.copyfile(request,root/'Temp/gltf-validation/direct-gpu/current-tests.json')
out=root/'Temp/gltf-validation/direct-gpu/point-finite-ownership-runs'/uuid.uuid4().hex
code=subprocess.call([sys.executable,str(root/'engine/Tools/Gltf/scripts/run_editor_probes.py'),'editor_direct_gpu_tests.csx','--pid',str(ready['pid']),'--project',ready['project'],'--output',str(out),'--timeout','600','--confirmed-discovery',ready['discoveryPath'],'--confirmed-sha256',ready['discoverySha256']],cwd=root)
receipts=[path for path in out.rglob('receipt-*.json') if 'expected_cases' in json.loads(path.read_text())]
if len(receipts)!=1:
 print(f'Expected one model receipt, got {len(receipts)} in {out}',file=sys.stderr)
 raise SystemExit(code or 1)
path=receipts[0]
data=json.loads(path.read_text())
print(json.dumps({'receipt':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'passed':data['passed'],'passed_cases':data['passed_cases'],'failed_cases':data['failed_cases'],'incomplete_cases':data['incomplete_cases'],'cleanup_errors':data['cleanup_errors']}),flush=True)
assert data['request_sha256']==hashlib.sha256(request.read_bytes()).hexdigest(), 'Receipt request mismatch'
expected=sum(suite['expected_cases'] for suite in spec['suites'])
if not data['passed'] or data['expected_cases']!=expected or data['passed_cases']!=expected or data['cleanup_errors']:
 code=code or 1
raise SystemExit(code)
