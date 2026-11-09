import hashlib, json
from pathlib import Path
root=Path('/home/damian/projects/sbox-public')
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_bytes())
assert ready['ready'] and ready['loaded']['graphicsAvailable'] and not ready['editorStatus']['IsCompiling']
modules={x['name']:x for x in ready['loaded']['modules']}
a=json.loads((root/'Temp/gltf-validation/direct-gpu/normal-corner-e12.json').read_bytes())
for key,name in [('engine_sha256','Sandbox.Engine'),('gltf_sha256','Sandbox.Gltf'),('tools_sha256','Sandbox.Tools')]:
 m=modules[name]; assert m['diskMvid']==m['loadedMvid'];a[key]=m['sha256']
paths=['engine/Tools/Gltf/direct-gpu/GltfGpuTriangleGateTests.cs','engine/Tools/Gltf/direct-gpu/models/Fox/GltfGpuBackendContractTests.cs','engine/Tools/Gltf/direct-gpu/models/Fox/GltfGpuRawTriangleChecks.cs','engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/GeneratedTangentSkinProbe.cs','engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/NormalCornerPublicComputeTests.cs','engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/NormalFlatSkinPublicComputeTests.cs','engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/NormalTangentPublicRendererTests.cs']
a['sources']=[{'path':p,'sha256':hashlib.sha256((root/p).read_bytes()).hexdigest()} for p in paths]
setup=[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']}]
a['suites']=[{'type':'Sandbox.Gltf.DirectGpu.NormalTangentTestValidation.NormalCornerPublicComputeTests','kind':'mstest','expected_cases':2,'setup':setup},{'type':'Sandbox.Gltf.DirectGpu.NormalTangentTestValidation.NormalFlatSkinPublicComputeTests','kind':'mstest','expected_cases':1,'setup':setup},{'type':'Sandbox.Gltf.DirectGpu.NormalTangentTestValidation.NormalTangentPublicRendererTests','kind':'mstest','expected_cases':4,'setup':setup,'measurements_property':'Measurements'}]
p=root/'Temp/gltf-validation/direct-gpu/normal-public-current-request.json';p.write_text(json.dumps(a,indent=2)+'\n')
print(ready['pid'],ready['discoveryPath'],ready['discoverySha256'])
