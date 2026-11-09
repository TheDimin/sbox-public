#!/usr/bin/env python3
"""Create matched material applicability witnesses and invoke pinned validator."""
import base64, copy, hashlib, json, struct, subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[6]
src=root/'Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF'
out=root/'Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/validator-uv';out.mkdir(parents=True,exist_ok=True)
j=json.loads((src/'MeshPrimitiveModes.gltf').read_text()); data=bytearray((src/'buffer.bin').read_bytes());data.extend(b''.join(struct.pack('<ffff',.25,.5,.75,.5) for _ in range(7)))
view=len(j['bufferViews']);j['bufferViews'].append({'buffer':0,'byteOffset':216,'byteLength':112,'byteStride':16,'target':34962})
first=len(j['accessors']);j['accessors'].extend([{'bufferView':view,'byteOffset':offset,'componentType':5126,'count':7,'type':'VEC2'} for offset in (0,8)])
j['meshes'][1]['primitives'][0]['attributes']['TEXCOORD_1']=first+1
j['meshes'][4]['primitives'][0]['attributes'].update(TEXCOORD_0=first,TEXCOORD_1=first+1)
for mesh in (1,4):j['meshes'][mesh]['primitives'][0]['material']=0
j['scenes'][0]['nodes']=[1,4];j['materials']=[{'pbrMetallicRoughness':{'baseColorTexture':{'index':0,'texCoord':1}},'normalTexture':{'index':0,'texCoord':0}}]
j['textures']=[{'source':0}];j['images']=[{'uri':'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGNocFBoUGhwcAAAC8kCgZHWr1kAAAAASUVORK5CYII='}]
j['buffers'][0]={'uri':'data:application/octet-stream;base64,'+base64.b64encode(data).decode(),'byteLength':len(data)}
valid=copy.deepcopy(j);valid['meshes'][1]['primitives'][0]['attributes']['TEXCOORD_0']=first
validator=root/'Temp/gltf-validation/validator/gltf_validator';print('validator_sha256',hashlib.sha256(validator.read_bytes()).hexdigest())
for label,obj in [('missing',j),('present',valid)]:
 p=out/(label+'.gltf');p.write_text(json.dumps(obj,indent=2)+'\n');r=subprocess.run([str(validator),'-o',str(p)],text=True,capture_output=True);(out/(label+'.report.json')).write_text(r.stdout);print(label,'exit',r.returncode,'sha256',hashlib.sha256(p.read_bytes()).hexdigest());report=json.loads(r.stdout);print(json.dumps(report['issues'],indent=2))
