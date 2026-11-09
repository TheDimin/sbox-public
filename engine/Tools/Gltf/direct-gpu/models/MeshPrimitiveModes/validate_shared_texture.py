#!/usr/bin/env python3
"""Reconstruct the mixed shared-slot texture witness and validate glTF structure."""
import base64,hashlib,json,struct,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[6];out=root/'Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/validator-uv';j=json.loads((out/'mixed-slot.gltf').read_text());data=bytearray(base64.b64decode(j['buffers'][0]['uri'].split(',',1)[1]));origin=len(data)
for v in range(7):
 x,y,_=struct.unpack_from('<fff',data,132+v*12);data+=struct.pack('<ffff',x*.4+.5,y*.4+.5,x*.2+.5,y*.2+.5)
for _ in range(7):data+=struct.pack('<ffff',1,0,0,1)
uv=len(j['bufferViews']);j['bufferViews'].append({'buffer':0,'byteOffset':origin,'byteLength':112,'byteStride':16,'target':34962});tan=len(j['bufferViews']);j['bufferViews'].append({'buffer':0,'byteOffset':origin+112,'byteLength':112,'target':34962});first=len(j['accessors']);j['accessors'] += [{'bufferView':uv,'byteOffset':x,'componentType':5126,'count':7,'type':'VEC2'} for x in (0,8)];tangent=len(j['accessors']);j['accessors'].append({'bufferView':tan,'componentType':5126,'count':7,'type':'VEC4'})
for i in (1,2,4):j['meshes'][i]['primitives'][0]['attributes'].update(TEXCOORD_0=first,TEXCOORD_1=first+1)
j['meshes'][2]['primitives'][0]['attributes']['TANGENT']=tangent;j['materials'][0]['pbrMetallicRoughness']['baseColorTexture']={'index':0,'texCoord':1};j['materials'][0]['normalTexture']={'index':0,'texCoord':0};j['textures']=[{'source':0}];j['images']=[{'uri':'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGNocFBoUGhwcAAAC8kCgZHWr1kAAAAASUVORK5CYII='}];j['buffers'][0]={'uri':'data:application/octet-stream;base64,'+base64.b64encode(data).decode(),'byteLength':len(data)}
p=out/'mixed-texture.gltf';p.write_text(json.dumps(j,indent=2)+'\n');v=root/'Temp/gltf-validation/validator/gltf_validator';r=subprocess.run([str(v),'-o',str(p)],text=True,capture_output=True);(out/'mixed-texture.report.json').write_text(r.stdout);report=json.loads(r.stdout);print('exit',r.returncode,'input_sha256',hashlib.sha256(p.read_bytes()).hexdigest(),'errors',report['issues']['numErrors'],'warnings',report['issues']['numWarnings']);print(json.dumps(report['issues']['messages'],indent=2))
