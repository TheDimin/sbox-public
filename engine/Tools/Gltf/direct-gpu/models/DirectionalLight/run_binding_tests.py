#!/usr/bin/env python3
# Run inside the shared native-test lock and the host resource guard.
import hashlib,json,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[6];here=Path(__file__).resolve().parent
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text());assert ready['ready']
host=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']));e=next(x for x in host['editors'] if x['pid']==ready['pid']);assert e['start']==ready['start'] and Path(e['project']).resolve()==Path(ready['project']).resolve()
files={'DirectionalBindingTests.cs':'c9aea0ea87493692c7046b0dd3d13730d9ca7caa018cac4340ef8d321b8ff258','DirectionalSceneSelectionTests.cs':'c1c0e94d818845dd0295f49ac20db6e37f511ca72b6da381c229c85a0b85f6ad'}
rows=[]
for name,expected in files.items():
 path=here/name;assert hashlib.sha256(path.read_bytes()).hexdigest()==expected;rows.append({'path':str(path.relative_to(root)),'sha256':expected})
modules={x['name']:x for x in ready['loaded']['modules']}
for row in modules.values():assert hashlib.sha256(Path(row['path']).read_bytes()).hexdigest()==row['sha256']
framework=root/'engine/Tests/Sandbox.Test.Gltf/bin/Release/net10.0/Microsoft.VisualStudio.TestPlatform.TestFramework.dll'
spec={'version':1,'engine_sha256':modules['Sandbox.Engine']['sha256'],'gltf_sha256':modules['Sandbox.Gltf']['sha256'],'tools_sha256':modules['Sandbox.Tools']['sha256'],'framework_sha256':hashlib.sha256(framework.read_bytes()).hexdigest(),'sources':rows,'suites':[{'type':'Sandbox.Gltf.DirectGpu.DirectionalLightValidation.DirectionalBindingTests','kind':'mstest','expected_cases':2},{'type':'Sandbox.Gltf.DirectGpu.DirectionalLightValidation.DirectionalSceneSelectionTests','kind':'mstest','expected_cases':1}]}
(root/'Temp/gltf-validation/direct-gpu/current-tests.json').write_text(json.dumps(spec,indent=2)+'\n')
out=root/'Temp/gltf-validation/direct-gpu/directional-binding-selected-stage-01'
code=subprocess.call([sys.executable,str(root/'engine/Tools/Gltf/scripts/run_editor_probes.py'),'editor_direct_gpu_tests.csx','--pid',str(ready['pid']),'--project',ready['project'],'--output',str(out),'--timeout','600','--confirmed-discovery',ready['discoveryPath'],'--confirmed-sha256',ready['discoverySha256']],cwd=root)
for path in out.rglob('receipt-*.json'):
 data=json.loads(path.read_text())
 if 'expected_cases' in data:
  print(json.dumps({'receipt':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'passed':data['passed'],'passed_cases':data['passed_cases'],'failed_cases':data['failed_cases'],'cleanup_errors':data['cleanup_errors']}),flush=True)
  if not data['passed']:code=1
raise SystemExit(code)
