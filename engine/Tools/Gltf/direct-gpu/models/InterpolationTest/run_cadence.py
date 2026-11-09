#!/usr/bin/env python3
"""Invoke only under the shared native flock and host resource guard."""
import hashlib,json,pathlib,shutil,subprocess,sys
root=pathlib.Path(__file__).resolve().parents[6]
owned=pathlib.Path(__file__).resolve().parent
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
host=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
editor=next(x for x in host['editors'] if x['pid']==ready['pid'])
assert editor['start']==ready['start'] and pathlib.Path(editor['project']).resolve()==pathlib.Path(ready['project']).resolve()
status=json.loads(subprocess.check_output([str(root/'game/bin/managed/sbox-cli'),'--connect',str(ready['pid']),'--call','editor_status'],cwd=str(pathlib.Path(ready['project']).parent)))
print('fresh editor_status:',json.dumps(status),flush=True)
assert not status['IsPlaying'] and not status['IsCompiling'] and status['LastCompileSucceeded'] and not status['SceneHasUnsavedChanges']
request=(pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/InterpolationTest') / 'cadence-request.json'
assert hashlib.sha256(request.read_bytes()).hexdigest()=='744c1b4a2c404f3918143afeb8cf32848d4ebc59afc882df2317b93b192d5acb'
shutil.copyfile(request,root/'Temp/gltf-validation/direct-gpu/current-tests.json')
out=root/'Temp/gltf-validation/direct-gpu/interpolation-cadence-run'
code=subprocess.call([sys.executable,str(root/'engine/Tools/Gltf/scripts/run_editor_probes.py'),'editor_direct_gpu_tests.csx','--pid',str(ready['pid']),'--project',ready['project'],'--output',str(out),'--timeout','600','--confirmed-discovery',ready['discoveryPath'],'--confirmed-sha256',ready['discoverySha256']],cwd=root)
for path in out.rglob('receipt-*.json'):
 data=json.loads(path.read_text()); print(json.dumps({'receipt':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),**{k:data.get(k) for k in ['passed','expected_cases','passed_cases','failed_cases','incomplete_cases','cleanup_errors','error']}}),flush=True)
 if not data.get('passed'): code=1
sys.exit(code)
