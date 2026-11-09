import hashlib, json, pathlib, subprocess, sys
root = pathlib.Path('/home/damian/projects/sbox-public')
expected = sys.argv[1]
marker = json.loads((root / 'Temp/gltf-validation/editor-session-ready.json').read_text())
modules = {m['name']: m for m in marker['loaded']['modules']}
assert marker['ready'] and modules['Sandbox.Engine']['loadedMvid'] == expected
base = json.loads((root / 'research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/mask-candidate-8fc-selection.json').read_text())
for key, name in [('engine_sha256','Sandbox.Engine'),('gltf_sha256','Sandbox.Gltf'),('tools_sha256','Sandbox.Tools')]:
    assert modules[name]['loadedMvid'] == modules[name]['diskMvid']
    base[key] = modules[name]['sha256']
prefix = 'research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/'
known = {'AlphaManualDispatchTests.cs':'30dd3f9118354354fe2ec2ecaae62a0cf6cab45821e853964539e9d34bd4a121'}
base['sources'] = []
base['suites'] = []
for name, digest in known.items():
    assert hashlib.sha256((root / prefix / name).read_bytes()).hexdigest() == digest
    base['sources'].append({'path':prefix+name,'sha256':digest})
    cls = name[:-3]
    base['suites'].append({'type':'Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation.'+cls,'kind':'mstest','expected_cases':1,'setup':[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']},{'kind':'property','name':'OutputDirectory','arguments':[str(root / 'Temp/gltf-validation/direct-gpu' / ('alpha-native-contract-'+cls+'-04'))]}],'measurements_property':'Measurements'})
raw = (json.dumps(base,indent=2)+'\n').encode()
(root / prefix / 'native-contract-selection.json').write_bytes(raw)
(root / 'Temp/gltf-validation/direct-gpu/current-tests.json').write_bytes(raw)
out = root / 'Temp/gltf-validation/direct-gpu/alpha-native-contract-run-04'
subprocess.run(['python3','engine/Tools/Gltf/scripts/run_editor_probes.py','editor_direct_gpu_tests.csx','--pid',str(marker['pid']),'--project',marker['project'],'--output',str(out),'--confirmed-discovery',marker['discoveryPath'],'--confirmed-sha256',marker['discoverySha256'],'--timeout','450'],cwd=root,check=True)
p = out / 'probe-01-editor_direct_gpu_tests/receipt-01.json'
b = p.read_bytes(); v = json.loads(b)
print(json.dumps({'receipt':str(p),'sha256':hashlib.sha256(b).hexdigest(),'phase':v['phase'],'passed':v['passed_cases'],'failed':v['failed_cases'],'incomplete':v['incomplete_cases'],'tests':v['tests'],'error':v['error']}))
