import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path('/home/damian/projects/sbox-public');expected=sys.argv[1];marker=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text());modules={x['name']:x for x in marker['loaded']['modules']};assert marker['ready'] and modules['Sandbox.Engine']['loadedMvid']==expected
base=json.loads((root/'research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/mask-normal-8fc-selection.json').read_text())
for key,name in [('engine_sha256','Sandbox.Engine'),('gltf_sha256','Sandbox.Gltf'),('tools_sha256','Sandbox.Tools')]: assert modules[name]['loadedMvid']==modules[name]['diskMvid'];base[key]=modules[name]['sha256']
prefix='research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/';name='AlphaCanonicalPublicRenderingTests.cs';sha='d926d23e3b64a0d890784f7d48851393f846fa998e171873083c66e7a1e22aa5';assert hashlib.sha256((root/prefix/name).read_bytes()).hexdigest()==sha
auditBytes=(root/prefix/'source-audit.json').read_bytes();assert hashlib.sha256(auditBytes).hexdigest()=='2bf334c2251080beacb66030afba514d6e1c77a93c2e961c994efbc07c3fd4fa'
audit=json.loads(auditBytes);fixture=root/'Temp/gltf-reference/glTF-Sample-Assets/Models/AlphaBlendModeTest'
for path,digest in audit['hashes'].items():
 if path.startswith('glTF/'):assert hashlib.sha256((fixture/path).read_bytes()).hexdigest()==digest,path
base['sources']=[s for s in base['sources'] if not s['path'].startswith(prefix)]+[{'path':prefix+name,'sha256':sha}]
for s in base['sources']:assert hashlib.sha256((root/s['path']).read_bytes()).hexdigest()==s['sha256']
base['suites']=[{'type':'Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation.AlphaCanonicalPublicRenderingTests','kind':'mstest','expected_cases':2,'setup':[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']}],'measurements_property':'Measurements'}]
raw=(json.dumps(base,indent=2)+'\n').encode();(root/prefix/'canonical-public-selection.json').write_bytes(raw);(root/'Temp/gltf-validation/direct-gpu/current-tests.json').write_bytes(raw);out=root/'Temp/gltf-validation/direct-gpu/alpha-canonical-public-run-01'
completed=subprocess.run(['python3','engine/Tools/Gltf/scripts/run_editor_probes.py','editor_direct_gpu_tests.csx','--pid',str(marker['pid']),'--project',marker['project'],'--output',str(out),'--confirmed-discovery',marker['discoveryPath'],'--confirmed-sha256',marker['discoverySha256'],'--timeout','600'],cwd=root,check=False)
p=out/'probe-01-editor_direct_gpu_tests/receipt-01.json';b=p.read_bytes();v=json.loads(b);print(json.dumps({'receipt':str(p),'sha256':hashlib.sha256(b).hexdigest(),'phase':v['phase'],'passed':v['passed_cases'],'failed':v['failed_cases'],'incomplete':v['incomplete_cases'],'tests':v['tests'],'error':v['error'],'runnerExit':completed.returncode}));sys.exit(completed.returncode)
