import hashlib,json,pathlib,sys
root=pathlib.Path(__file__).resolve().parents[7];d=pathlib.Path(__file__).resolve().parent;b='research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision/'
paths=[b+'Png16StreamRejectionTests.cs',b+'Png16GpuPrecisionNegativeControls.cs',b+'Png16BudgetBoundaryTests.cs']; extras=[b+'invalid-stream-fixtures.json','engine/Sandbox.Engine/Resources/Model/Gltf/GltfTextureCompiler.cs','engine/Sandbox.Engine/Resources/Model/Gltf/GltfPng16Decoder.cs']
def sha(p):return hashlib.sha256((root/p).read_bytes()).hexdigest()
freeze=(pathlib.Path(__file__).resolve().parents[7]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision') / 'followup-freeze.json'
if '--request' not in sys.argv:
 freeze.write_text(json.dumps({'sources':[{'path':p,'sha256':sha(p)} for p in paths],'inputs':[{'path':p,'sha256':sha(p)} for p in extras]},indent=2)+'\n');print('freeze',sha(str(freeze.relative_to(root))));sys.exit()
f=json.loads(freeze.read_text())
for item in f['sources']+f['inputs']:assert sha(item['path'])==item['sha256'],item['path']
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text());mods={x['name']:x['sha256'] for x in ready['loaded']['modules']};assert mods['Sandbox.Engine']=='60bc2b51bbd70c40a000f8b29f420714aff4e3dbda723794b18fdfc43b571396', 'wrong engine stage'
ns='Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation.'
r={'version':1,'engine_sha256':mods['Sandbox.Engine'],'gltf_sha256':mods['Sandbox.Gltf'],'tools_sha256':mods['Sandbox.Tools'],'framework_sha256':'81048167804a8d178ecd712dae394aca647bd1560c0406f3eabe2e596b465fad','sources':f['sources'],'suites':[{'type':ns+'Png16StreamRejectionTests','kind':'static_results','method':'Run','arguments':['$checkout'],'expected_cases':3},{'type':ns+'Png16GpuPrecisionNegativeControls','kind':'mstest','expected_cases':1},{'type':ns+'Png16BudgetBoundaryTests','kind':'mstest','expected_cases':1}]}
((pathlib.Path(__file__).resolve().parents[7]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision') / 'followup-request.json').write_text(json.dumps(r,indent=2)+'\n');print('request',sha(str(((pathlib.Path(__file__).resolve().parents[7]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision') / 'followup-request.json').relative_to(root))))
