import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path('/home/damian/projects/sbox-public');expected=sys.argv[1];marker=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text());modules={x['name']:x for x in marker['loaded']['modules']};assert marker['ready'] and modules['Sandbox.Engine']['loadedMvid']==expected
base=json.loads((root/'research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/mask-candidate-8fc-selection.json').read_text())
for key,name in [('engine_sha256','Sandbox.Engine'),('gltf_sha256','Sandbox.Gltf'),('tools_sha256','Sandbox.Tools')]:assert modules[name]['loadedMvid']==modules[name]['diskMvid'];base[key]=modules[name]['sha256']
prefix='research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/'
known={'AlphaNativeVmdlRegressionTests.cs':'cc054104edc97aee605f741dee02e2e5f2e2ff039ea23a98e0e5cacb44a64d13','AlphaFramebufferOwnershipTests.cs':'fafdaa163fddf984dea2fe7f0c5d95c336050862284c6d09afa4432e91804b59','AlphaSortObjectStateTests.cs':'871b3c4b07865bf85c22b2f41d98ce8afbeaadef3365449075f3f21e39c2581e','AlphaCallbackFallbackTests.cs':'6e8c7c70477251b65391898a0cd9a03cc18011614e866a6b43de813a5ff7f460','AlphaCallbackFallbackGpuTests.cs':'5bb120ed35312ed0d6f8c3743ce9b3a4ffc96b4843def19a2728407e5ad9a6a3','AlphaCrossInstanceViewTests.cs':'0947d75a344475210884f442cd572decfec6810b406edfb905af2ada4df9ee5d','AlphaControlFixtures.cs':'07b5c4ed3e67354fd156d252940a947545e2472dd4b70d833741abf2dc80080b','AlphaCrossModelOracle.cs':'f3d023c4bcb7c87e4f014ee2b40d9f21ba784c3b42dfa9023685cf3b1252e472'}
base['sources']=[];base['suites']=[]
for name,sha in known.items():assert hashlib.sha256((root/prefix/name).read_bytes()).hexdigest()==sha;base['sources'].append({'path':prefix+name,'sha256':sha})
shader='1a65f5c77ab9424972754e2d4c1a88a94ba16b56d76a9f7e138b639d2c5ce8f1';assert hashlib.sha256((root/prefix/'gltf_lit_alpha_current_candidate.shader').read_bytes()).hexdigest()==shader
for cls,count in [('AlphaNativeVmdlRegressionTests',1),('AlphaFramebufferOwnershipTests',1),('AlphaSortObjectStateTests',2),('AlphaCallbackFallbackTests',1),('AlphaCallbackFallbackGpuTests',1),('AlphaCrossInstanceViewTests',1)]:
 suite={'type':'Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation.'+cls,'kind':'mstest','expected_cases':count}
 if cls in ('AlphaNativeVmdlRegressionTests','AlphaCallbackFallbackGpuTests','AlphaCrossInstanceViewTests'):
  setup=[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']},{'kind':'property','name':'OutputDirectory','arguments':[str(root/'Temp/gltf-validation/direct-gpu'/('alpha-sort-candidate-'+cls+'-01'))]}]
  if cls=='AlphaCrossInstanceViewTests':setup.append({'kind':'property','name':'ShaderSha256','arguments':[shader]})
  suite.update(setup=setup,measurements_property='Measurements')
 base['suites'].append(suite)
raw=(json.dumps(base,indent=2)+'\n').encode();(root/prefix/'sort-candidate-selection.json').write_bytes(raw);(root/'Temp/gltf-validation/direct-gpu/current-tests.json').write_bytes(raw);out=root/'Temp/gltf-validation/direct-gpu/alpha-sort-candidate-run-01'
completed=subprocess.run(['python3','engine/Tools/Gltf/scripts/run_editor_probes.py','editor_direct_gpu_tests.csx','--pid',str(marker['pid']),'--project',marker['project'],'--output',str(out),'--confirmed-discovery',marker['discoveryPath'],'--confirmed-sha256',marker['discoverySha256'],'--timeout','900'],cwd=root,check=False)
p=out/'probe-01-editor_direct_gpu_tests/receipt-01.json';b=p.read_bytes();v=json.loads(b);print(json.dumps({'receipt':str(p),'sha256':hashlib.sha256(b).hexdigest(),'phase':v['phase'],'passed':v['passed_cases'],'failed':v['failed_cases'],'incomplete':v['incomplete_cases'],'tests':v['tests'],'error':v['error'],'runnerExit':completed.returncode}));sys.exit(completed.returncode)
