#!/usr/bin/env python3
# Called only inside the shared lock and host guard.
import hashlib,json,shutil,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[6]; here=Path(__file__).resolve().parent
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
host=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
editor=next(e for e in host['editors'] if e['pid']==ready['pid'])
assert editor['start']==ready['start'] and Path(editor['project']).resolve()==Path(ready['project']).resolve()
request=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/DirectionalLight') / 'rendered-request.json'; spec=json.loads(request.read_text())
for item in spec['sources']: assert hashlib.sha256((root/item['path']).read_bytes()).hexdigest()==item['sha256']
shutil.copyfile(request,root/'Temp/gltf-validation/direct-gpu/current-tests.json')
out=root/'Temp/gltf-validation/direct-gpu/directional-rendered-8fc-01'
code=subprocess.call([sys.executable,str(root/'engine/Tools/Gltf/scripts/run_editor_probes.py'),'editor_direct_gpu_tests.csx','--pid',str(ready['pid']),'--project',ready['project'],'--output',str(out),'--timeout','600','--confirmed-discovery',ready['discoveryPath'],'--confirmed-sha256',ready['discoverySha256']],cwd=root)
for path in out.rglob('receipt-*.json'):
 data=json.loads(path.read_text())
 if 'expected_cases' in data:
  print(json.dumps({'receipt':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'passed':data['passed'],'passed_cases':data['passed_cases'],'failed_cases':data['failed_cases'],'cleanup_errors':data['cleanup_errors']}),flush=True)
  if not data['passed']: code=1
raise SystemExit(code)
