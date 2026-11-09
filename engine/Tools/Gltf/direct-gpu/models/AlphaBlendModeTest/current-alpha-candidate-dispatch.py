import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path('/home/damian/projects/sbox-public');expected=sys.argv[1];marker=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text());modules={x['name']:x for x in marker['loaded']['modules']};assert marker['ready'] and modules['Sandbox.Engine']['loadedMvid']==expected
base=json.loads((root/'research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/mask-normal-8fc-selection.json').read_text())
for key,name in [('engine_sha256','Sandbox.Engine'),('gltf_sha256','Sandbox.Gltf'),('tools_sha256','Sandbox.Tools')]:
 assert modules[name]['loadedMvid']==modules[name]['diskMvid'];base[key]=modules[name]['sha256']
prefix='research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/'
known={'AlphaCurrentCoverageTests.cs':'7e356c111b10d57db88a0a6f0ed5e34c8f16d3e57f79fb4012e3fd696c63735d','AlphaCurrentSortingTests.cs':'29f842cdb0df4d7fc1c1ef2835a5dcd39504df532c76e6241e587657a82eb460','AlphaCurrentBackfaceTests.cs':'b19e0a5a90300ca21ad6f9ad71cca937cd4cf9e82371bd68eb0cf3b48e5772b0','AlphaControlFixtures.cs':'07b5c4ed3e67354fd156d252940a947545e2472dd4b70d833741abf2dc80080b'}
base['sources']=[x for x in base['sources'] if not x['path'].startswith(prefix)]
for name,sha in known.items():
 assert hashlib.sha256((root/prefix/name).read_bytes()).hexdigest()==sha;base['sources'].append({'path':prefix+name,'sha256':sha})
shader='1a65f5c77ab9424972754e2d4c1a88a94ba16b56d76a9f7e138b639d2c5ce8f1';assert hashlib.sha256((root/prefix/'gltf_lit_alpha_current_candidate.shader').read_bytes()).hexdigest()==shader
base['suites']=[]
for cls,count in [('AlphaCurrentCoverageTests',1),('AlphaCurrentSortingTests',1),('AlphaCurrentBackfaceTests',2)]:
 setup=[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']}]
 if count==1: setup.extend([{'kind':'property','name':'ShaderSha256','arguments':[shader]},{'kind':'property','name':'OutputDirectory','arguments':[str(root/'Temp/gltf-validation/direct-gpu'/('alpha-current-'+cls+'-01'))]}])
 base['suites'].append({'type':'Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation.'+cls,'kind':'mstest','expected_cases':count,'setup':setup,'measurements_property':'Measurements'})
raw=(json.dumps(base,indent=2)+'\n').encode();(root/prefix/'current-alpha-candidate-selection.json').write_bytes(raw);(root/'Temp/gltf-validation/direct-gpu/current-tests.json').write_bytes(raw);out=root/'Temp/gltf-validation/direct-gpu/alpha-current-candidate-run-01'
completed=subprocess.run(['python3','engine/Tools/Gltf/scripts/run_editor_probes.py','editor_direct_gpu_tests.csx','--pid',str(marker['pid']),'--project',marker['project'],'--output',str(out),'--confirmed-discovery',marker['discoveryPath'],'--confirmed-sha256',marker['discoverySha256'],'--timeout','900'],cwd=root,check=False)
p=out/'probe-01-editor_direct_gpu_tests/receipt-01.json';b=p.read_bytes();v=json.loads(b);print(json.dumps({'receipt':str(p),'sha256':hashlib.sha256(b).hexdigest(),'phase':v['phase'],'passed':v['passed_cases'],'failed':v['failed_cases'],'incomplete':v['incomplete_cases'],'tests':v['tests'],'error':v['error'],'runnerExit':completed.returncode}));sys.exit(completed.returncode)
