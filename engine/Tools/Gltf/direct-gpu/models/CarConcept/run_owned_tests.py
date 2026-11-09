#!/usr/bin/env python3
"""Execute only under the shared flock followed by the host resource guard."""
import hashlib,json,shutil,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[6]
owned=Path(__file__).resolve().parent
request=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/CarConcept')/sys.argv[1]
output=root/'Temp/gltf-validation/direct-gpu'/sys.argv[2]
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
assert ready['ready']
host=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
editor=next(row for row in host['editors'] if row['pid']==ready['pid'])
assert editor['start']==ready['start'] and Path(editor['project']).resolve()==Path(ready['project']).resolve(), 'Editor identity changed'
audit=json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/CarConcept') / 'source-audit.json').read_text())
def verify_inputs():
 for row in audit['files']:
  assert hashlib.sha256((root/row['path']).read_bytes()).hexdigest()==row['sha256'], 'Pinned model input changed: '+row['path']
verify_inputs()
for module in ready['loaded']['modules']:
 assert module['loadedMvid']==module['diskMvid'] and hashlib.sha256(Path(module['path']).read_bytes()).hexdigest()==module['sha256'], 'Loaded/disk producer changed: '+module['name']
status=json.loads(subprocess.check_output([str(root/'game/bin/managed/sbox-cli'),'--connect',str(ready['pid']),'--call','editor_status'],text=True))
assert Path(status['Paths']['ProjectRoot']).resolve()==Path(ready['project']).resolve().parent and not status['IsPlaying'] and not status['IsCompiling'] and status['LastCompileSucceeded'], 'Editor is not ready'
assert not output.exists(), 'Capture output already exists'
shutil.copyfile(request,root/'Temp/gltf-validation/direct-gpu/current-tests.json')
code=subprocess.call([sys.executable,str(root/'engine/Tools/Gltf/scripts/run_editor_probes.py'),'editor_direct_gpu_tests.csx','--pid',str(ready['pid']),'--project',ready['project'],'--output',str(output),'--timeout','600','--confirmed-discovery',ready['discoveryPath'],'--confirmed-sha256',ready['discoverySha256']],cwd=root)
verify_inputs()
(output/'model-source-audit.json').write_text(json.dumps(audit,indent=2)+'\n')
(output/'producer-session-ready.json').write_text(json.dumps(ready,indent=2)+'\n')
receipts=list(output.rglob('receipt-*.json'))
for path in receipts:
 value=json.loads(path.read_text())
 if 'expected_cases' in value:
  print(json.dumps({'receipt':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),**{k:value.get(k) for k in ['passed','expected_cases','passed_cases','failed_cases','incomplete_cases','cleanup_errors','error']}}),flush=True)
  if not value.get('passed'):code=1
raise SystemExit(code)
