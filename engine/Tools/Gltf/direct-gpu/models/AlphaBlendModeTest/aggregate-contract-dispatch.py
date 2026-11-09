import hashlib, json, pathlib, subprocess, sys
root=pathlib.Path('/home/damian/projects/sbox-public')
expected=sys.argv[1]
marker=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
modules={m['name']:m for m in marker['loaded']['modules']}
assert marker['ready'] and modules['Sandbox.Engine']['loadedMvid']==expected
for name in ('Sandbox.Engine','Sandbox.Gltf','Sandbox.Tools'):
 assert modules[name]['loadedMvid']==modules[name]['diskMvid']
prefix='research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/'
base=json.loads((root/prefix/'mask-candidate-8fc-selection.json').read_text())
for key,name in [('engine_sha256','Sandbox.Engine'),('gltf_sha256','Sandbox.Gltf'),('tools_sha256','Sandbox.Tools')]: base[key]=modules[name]['sha256']
base['sources']=[];base['suites']=[];seen={}
def add_source(entry):
 path=entry['path'];sha=entry['sha256'];assert hashlib.sha256((root/path).read_bytes()).hexdigest()==sha,path
 if path in seen: assert seen[path]==sha,path;return
 seen[path]=sha;base['sources'].append(entry)
known={'AlphaManualDispatchTests.cs':'73689886abbde75c52f2583ba0563fc0b68af6248f1f7e026e623e305fd706c5','AlphaNativeCallbackContractTests.cs':'285474eacb51ab1d32d2a1c72a351d8900a8bd6c2ad2e474bfe915208b8e4963','AlphaNativeVmdlMutationTests.cs':'1685ceef8bc9ea95f770b1a8c359a0bd62e7c2f8ad8e2ddf143db3b76f308c29'}
for name,sha in known.items():
 add_source({'path':prefix+name,'sha256':sha});cls=name[:-3]
 base['suites'].append({'type':'Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation.'+cls,'kind':'mstest','expected_cases':1,'setup':[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']},{'kind':'property','name':'OutputDirectory','arguments':[str(root/'Temp/gltf-validation/direct-gpu'/('alpha-native-contract-'+cls+'-01'))]}],'measurements_property':'Measurements'})
requests=[('research/gltf-validation/direct-gpu/models/Cameras/flip-request.json','3fb81eed204de77455b35070a3d8f47f6ef9800a20636c56456b5969bc751255','Sandbox.Gltf.DirectGpu.CamerasValidation.CameraNativeFlipTests'),('research/gltf-validation/direct-gpu/models/MeshPrimitiveModes/combined-full-current-request.json','c8e88ebeddafb32ebeacf5bae188e9e0de467a64c598f09aa10aa33ae30dffb2','Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.SharedSlotMutationBaseline'),('research/gltf-validation/direct-gpu/models/PointLightIntensityTest/request.json','6ba8cf38a97213cc68a5bc64b411514380c1040f1df037bf79ef1b33e8aec316','Sandbox.Gltf.DirectGpu.PointLightIntensityValidation.PointPhotometryTests')]
for path,sha,selected in requests:
 data=(root/path).read_bytes();assert hashlib.sha256(data).hexdigest()==sha,path;req=json.loads(data);assert req['framework_sha256']==base['framework_sha256']
 for entry in req['sources']:add_source(entry)
 suites=[s for s in req['suites'] if s['type']==selected];assert len(suites)==1 and suites[0]['expected_cases']==1;base['suites'].append(suites[0])
assert len(base['sources'])==18 and len(base['suites'])==6
raw=(json.dumps(base,indent=2)+'\n').encode();(root/prefix/'aggregate-contract-selection.json').write_bytes(raw);(root/'Temp/gltf-validation/direct-gpu/current-tests.json').write_bytes(raw)
out=root/'Temp/gltf-validation/direct-gpu/alpha-aggregate-contract-run-01'
completed=subprocess.run(['python3','engine/Tools/Gltf/scripts/run_editor_probes.py','editor_direct_gpu_tests.csx','--pid',str(marker['pid']),'--project',marker['project'],'--output',str(out),'--confirmed-discovery',marker['discoveryPath'],'--confirmed-sha256',marker['discoverySha256'],'--timeout','750'],cwd=root,check=False)
p=out/'probe-01-editor_direct_gpu_tests/receipt-01.json';b=p.read_bytes();v=json.loads(b);print(json.dumps({'receipt':str(p),'sha256':hashlib.sha256(b).hexdigest(),'phase':v['phase'],'passed':v['passed_cases'],'failed':v['failed_cases'],'incomplete':v['incomplete_cases'],'tests':v['tests'],'error':v['error'],'runnerExit':completed.returncode}))
if completed.returncode: sys.exit(completed.returncode)
