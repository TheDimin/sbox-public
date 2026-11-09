#!/usr/bin/env python3
"""Run after integration publishes a fresh ready marker; caller holds native flock and guard."""
import hashlib,json,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[6]
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
assert ready['ready'] and ready['loaded']['modules']
expected_engine=sys.argv[1]
modules={m['name']:m for m in ready['loaded']['modules']}
assert modules['Sandbox.Engine']['sha256']==expected_engine, 'Unexpected producer; pass the new stage engine SHA'
host=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
e=next(x for x in host['editors'] if x['pid']==ready['pid'])
assert e['start']==ready['start'] and Path(e['project']).resolve()==Path(ready['project']).resolve()
files={'engine/Tools/Gltf/direct-gpu/models/MaterialsVariantsShoe/ShoePublicSelectionTests.cs':'489578edf30132e2f8cd231eb836085f7881ad67854b7e5155503fba038da2a4','engine/Tools/Gltf/direct-gpu/models/MaterialsVariantsShoe/ShoePublicCaptureTests.cs':'ff77b54a57e0a6a069ec188310024b098b8771a943e2ee04887a1c2b5b97a008','engine/Tools/Gltf/direct-gpu/models/MaterialsVariantsShoe/GltfOverridePrecedenceTests.cs':'fa792f51ebb049160f2e41db261708179541bb831eadec8bf673a5ac8fa54a32','engine/Tools/Gltf/direct-gpu/models/MaterialsVariantsShoe/NativeAccessorRollbackTests.cs':'142ee3088f5302b7cee3a217265265c2b960f386d3f4dade06c397f54f262f71','engine/Tools/Gltf/direct-gpu/models/MaterialsVariantsShoe/ShoeBindingDiagnosticTests.cs':'c16092084c658638a04b127a30e7b36c283f2c98859e2dbaefe833ee5a2f4bf8','engine/Tools/Gltf/direct-gpu/GltfGpuTriangleGateTests.cs':'35cf8663b61fe62dde41a3f5c157e555889ac0b8122e59d591349cbd64139161','engine/Tools/Gltf/direct-gpu/models/Fox/GltfGpuRawTriangleChecks.cs':'f63b4c12381f18d5da92727c94729ae7b7a72dacac4abfffb2d5bb66b23e3ffa','engine/Tools/Gltf/direct-gpu/models/Fox/GltfGpuBackendContractTests.cs':'304477b17bdb20494ca0c89c3a8b738302cb08a71825e81821ebc85a04eff544'}
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
for p,h in files.items():assert sha(root/p)==h,p
ref=json.loads((root/'research/gltf-validation/direct-gpu/models/MaterialsVariantsShoe/reference.json').read_text())
for form in ref['forms'].values():
 for name,entry in form['files'].items():assert sha(root/'Temp/gltf-reference/glTF-Sample-Assets/Models/MaterialsVariantsShoe'/name)==entry['sha256'],name
framework=root/'engine/Tests/Sandbox.Test.Gltf/bin/Release/net10.0/Microsoft.VisualStudio.TestPlatform.TestFramework.dll'
request={'version':1,'engine_sha256':modules['Sandbox.Engine']['sha256'],'gltf_sha256':modules['Sandbox.Gltf']['sha256'],'framework_sha256':sha(framework),'sources':[{'path':p,'sha256':h} for p,h in files.items()],'suites':[{'type':'Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation.ShoeBindingDiagnosticTests','kind':'mstest','expected_cases':1,'setup':[{'kind':'method','name':'Configure','arguments':['$checkout']}],'measurements_property':'Measurements'}]}
output=root/'Temp/gltf-validation/direct-gpu'/sys.argv[2]; assert not output.exists()
(root/'Temp/gltf-validation/direct-gpu/current-tests.json').write_text(json.dumps(request,indent=2)+'\n')
status=subprocess.run([str(root/'game/bin/managed/sbox-cli'),'--connect',str(ready['pid']),'--call','editor_status'],capture_output=True,text=True,check=True)
s=json.loads(status.stdout); assert s['Paths']['ProjectRoot']==str(Path(ready['project']).parent) and not s['IsPlaying'] and not s['IsCompiling'] and s['LastCompileSucceeded']
cmd=[sys.executable,str(root/'engine/Tools/Gltf/scripts/run_editor_probes.py'),'editor_direct_gpu_tests.csx','--pid',str(ready['pid']),'--project',ready['project'],'--output',str(output),'--confirmed-discovery',ready['discoveryPath'],'--confirmed-sha256',ready['discoverySha256'],'--timeout','600']
code=subprocess.call(cmd,cwd=root)
receipts=list(output.rglob('receipt-*.json'))
assert len(receipts)==1,receipts
p=receipts[0];r=json.loads(p.read_text()); print(json.dumps({'receipt':str(p),'sha256':sha(p),'passed':r['passed'],'passed_cases':r['passed_cases'],'failed_cases':r['failed_cases'],'incomplete_cases':r['incomplete_cases'],'error':r['error'],'cleanup_errors':r['cleanup_errors']}),flush=True)
if code or not r['passed']:raise SystemExit(1)
