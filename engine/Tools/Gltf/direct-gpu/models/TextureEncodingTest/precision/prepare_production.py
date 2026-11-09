"""Freeze source hashes now; invoke --request only after coordinator's new producer install."""
import hashlib,json,pathlib,sys
root=pathlib.Path(__file__).resolve().parents[7];d=pathlib.Path(__file__).resolve().parent;b='research/gltf-validation/direct-gpu/models/'
paths=[b+'BoxTextured/BoxTexturedReference.cs',b+'BoxTextured/PngTexelReference.cs']+[b+'TextureEncodingTest/precision/'+x for x in ['Png16ProductionTests.cs','Png16ProductionMatrixTests.cs','Png16ProductionBoundaryTests.cs','Png16GpuProducerTests.cs','Png16PublicModelTests.cs','Png16MetadataTests.cs','CoreImageMatrixTests.cs']]
extras=[b+'TextureEncodingTest/precision/managed-fixtures.json',b+'TextureEncodingTest/precision/metadata-fixture.json','engine/Sandbox.Engine/Resources/Model/Gltf/GltfTextureCompiler.cs','engine/Sandbox.Engine/Resources/Model/Gltf/GltfPng16Decoder.cs','engine/Tests/Sandbox.Test.Integration/Engine/GltfCompiledTextureTests.cs']
def sha(p):return hashlib.sha256((root/p).read_bytes()).hexdigest()
freeze=(pathlib.Path(__file__).resolve().parents[7]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision') / 'production-freeze.json'
if '--request' not in sys.argv:
 freeze.write_text(json.dumps({'sources':[{ 'path':p,'sha256':sha(p)} for p in paths], 'inputs':[{ 'path':p,'sha256':sha(p)} for p in extras]},indent=2)+'\n');print('freeze',sha(str(freeze.relative_to(root))));sys.exit()
f=json.loads(freeze.read_text())
for item in f['sources']+f['inputs']:assert sha(item['path'])==item['sha256'],item['path']
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text());mods={x['name']:x['sha256'] for x in ready['loaded']['modules']}
assert mods['Sandbox.Engine'] == '60bc2b51bbd70c40a000f8b29f420714aff4e3dbda723794b18fdfc43b571396', 'wrong engine stage'
ns='Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation.'
def static(cls,method,count,args=[]):return {'type':ns+cls,'kind':'static_results','method':method,'arguments':args,'expected_cases':count}
def test(cls,count,setup=[]):return {'type':ns+cls,'kind':'mstest','expected_cases':count,'setup':setup}
r={'version':1,'engine_sha256':mods['Sandbox.Engine'],'gltf_sha256':mods['Sandbox.Gltf'],'tools_sha256':mods['Sandbox.Tools'],'framework_sha256':'81048167804a8d178ecd712dae394aca647bd1560c0406f3eabe2e596b465fad','sources':f['sources'],'suites':[static('Png16ProductionMatrixTests','Run',46,['$checkout']),static('Png16ProductionTests','Run',5),static('Png16ProductionTests','RunProducerMip',2),test('Png16ProductionBoundaryTests',2),test('Png16GpuProducerTests',1),test('Png16PublicModelTests',1),test('Png16MetadataTests',1,[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']}]),static('CoreImageMatrixTests','Run',3)]}
((pathlib.Path(__file__).resolve().parents[7]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision') / 'production-request.json').write_text(json.dumps(r,indent=2)+'\n');print('request',sha(str(((pathlib.Path(__file__).resolve().parents[7]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision') / 'production-request.json').relative_to(root))))
