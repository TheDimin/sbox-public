"""Freeze only owned changed tests against the current verified producer marker."""
import hashlib,json,pathlib
root=pathlib.Path(__file__).resolve().parents[6];owned=pathlib.Path(__file__).resolve().parent
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text()); assert ready['ready']
mods={x['name']:x['sha256'] for x in ready['loaded']['modules']}
common={'version':1,'engine_sha256':mods['Sandbox.Engine'],'gltf_sha256':mods['Sandbox.Gltf'],'tools_sha256':mods['Sandbox.Tools'],'framework_sha256':'81048167804a8d178ecd712dae394aca647bd1560c0406f3eabe2e596b465fad'}
def source(p):
 f=root/p;return {'path':p,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()}
def make(name,paths,suite):
 value={**common,'sources':[source(p) for p in paths],'suites':[suite]};((pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/ABeautifulGame')/name).write_text(json.dumps(value,indent=2)+'\n')
base='research/gltf-validation/direct-gpu/'
helpers=[base+'GltfGpuTriangleGateTests.cs',base+'models/Fox/GltfGpuBackendContractTests.cs',base+'models/Fox/GltfGpuRawTriangleChecks.cs']
model=base+'models/ABeautifulGame/'
def mstest(typ,count):return {'type':'Sandbox.Gltf.DirectGpu.ABeautifulGameValidation.'+typ,'kind':'mstest','expected_cases':count,'setup':[{'kind':'method','name':'Configure','arguments':['$checkout']}],'measurements_property':'Measurements'}
make('next-policy-request.json',['engine/Tests/Sandbox.Test.Unit/Resources/GltfModelDocumentTests.cs',model+'LoaderPolicyTests.cs'],{'type':'Sandbox.Gltf.DirectGpu.ABeautifulGameValidation.LoaderPolicyTests','kind':'static_results','method':'RunAll','arguments':[],'expected_cases':7})
for name,typ,count in [('next-residency-request.json','BasisResidencyTests',1),('next-sampling-request.json','BasisSamplingTests',1),('next-framed-one-request.json','PublicFramedOneTests',1),('next-framed-all-request.json','PublicFramedTests',4),('next-public-basis-request.json','PublicBasisCaptureTests',2),('next-presentation-settle-request.json','PresentationSettleTests',1),('next-basis-presentation-request.json','BasisPresentationTests',1)]:make(name,helpers+[model+typ+'.cs'],mstest(typ,count))
print(json.dumps({'pid':ready['pid'],'engine_sha256':mods['Sandbox.Engine'],'files':['next-policy-request.json','next-residency-request.json','next-sampling-request.json','next-framed-one-request.json','next-framed-all-request.json','next-public-basis-request.json','next-presentation-settle-request.json','next-basis-presentation-request.json']}))
