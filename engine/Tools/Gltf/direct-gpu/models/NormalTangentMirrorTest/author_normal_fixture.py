"""Literal asymmetric UV/normal fixture and independent expected directions.

Four asymmetric PNG texels appear twice: W=+1 and W=-1. UV0 deliberately
selects a wrong constant sample; normal UV1 has V reversed, undone by the
declared texture transform. Neither green inversion nor a V flip can silently
cancel another incorrect implementation across this fixture's eight cells.
"""
import base64
import binascii
import json
import math
import struct
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
TEXELS = [(191,64,255),(64,191,255),(223,159,223),(32,96,255)]


def expected(rgb, w, back=False, scale=.5):
    x,y,z = [(v/255)*2-1 for v in rgb]
    # glTF N=+Z,T=+X -> engine N=+X,T=+Y,B=W*+Z.
    v = [z,x*scale,y*scale*w]
    length = math.sqrt(sum(c*c for c in v))
    return [c/length*(-1 if back else 1) for c in v]


def png():
    def chunk(kind,data): return struct.pack('>I',len(data))+kind+data+struct.pack('>I',binascii.crc32(kind+data)&0xffffffff)
    raw = b''.join(b'\0'+bytes(sum((list(t) for t in TEXELS[y*2:y*2+2]),[])) for y in range(2))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',2,2,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b'')


def build(baked_reference=False):
    data = bytearray(); views=[]; accessors=[]
    def stream(values,components):
        offset=len(data)
        for row in values: data.extend(struct.pack('<'+'f'*components,*row))
        views.append({'buffer':0,'byteOffset':offset,'byteLength':len(data)-offset})
        accessors.append({'bufferView':len(views)-1,'componentType':5126,'count':len(values),'type':{2:'VEC2',3:'VEC3',4:'VEC4'}[components]})
        return len(accessors)-1
    positions=[]; normals=[]; tangents=[]; uv0=[]; uv1=[]; indices=[]; cells=[]
    for hand in range(2):
        for texel in range(4):
            cell=hand*4+texel; x=(texel-1.5)*1.1; y=(.5-hand)*1.1; w=1 if hand==0 else -1
            u=(texel%2+.5)/2; v=(texel//2+.5)/2
            en=expected(TEXELS[texel],w)
            normal=[en[1],en[2],en[0]] if baked_reference else [0,0,1]
            tangent=[normal[2],0,-normal[0]] if baked_reference else [1,0,0]
            length=math.sqrt(sum(c*c for c in tangent));tangent=[c/length for c in tangent]
            for dx,dy in [(-.45,-.45),(.45,-.45),(.45,.45),(-.45,.45)]:
                positions.append([x+dx,y+dy,0]); normals.append(normal); tangents.append(tangent+[w]); uv0.append([.25,.25]); uv1.append([u,1-v])
            indices.extend([cell*4+i for i in (0,1,2,0,2,3)])
            cells.append({'cell':cell,'center_source':[x,y,0],'texel':texel,'rgb8':TEXELS[texel],'w':w,'uv1':[u,1-v],'transformed_uv':[u,v],
                          'front_engine_normal':expected(TEXELS[texel],w),'back_engine_normal':expected(TEXELS[texel],w,True)})
    attrs={name:stream(values,components) for name,values,components in [('POSITION',positions,3),('NORMAL',normals,3),('TANGENT',tangents,4),('TEXCOORD_0',uv0,2),('TEXCOORD_1',uv1,2)]}
    accessors[0]['min']=[min(p[c] for p in positions) for c in range(3)];accessors[0]['max']=[max(p[c] for p in positions) for c in range(3)]
    offset=len(data);data.extend(struct.pack('<'+'H'*len(indices),*indices));views.append({'buffer':0,'byteOffset':offset,'byteLength':len(data)-offset});accessors.append({'bufferView':len(views)-1,'componentType':5123,'count':len(indices),'type':'SCALAR'})
    document={'asset':{'version':'2.0','generator':'Independent NormalTangentMirror literal fixture'},'extensionsUsed':['KHR_texture_transform'],'scene':0,'scenes':[{'nodes':[0]}],'nodes':[{'mesh':0}],
              'meshes':[{'primitives':[{'attributes':attrs,'indices':5,'material':0}]}],
              'materials':[{'doubleSided':True,'pbrMetallicRoughness':{'baseColorFactor':[1,1,1,1],'metallicFactor':0,'roughnessFactor':1},'normalTexture':{'index':0,'scale':.5,'extensions':{'KHR_texture_transform':{'texCoord':1,'offset':[0,1],'scale':[1,-1]}}}}],
              'textures':[{'source':0,'sampler':0}],'samplers':[{'magFilter':9728,'minFilter':9728,'wrapS':33071,'wrapT':33071}],
              'images':[{'uri':'data:image/png;base64,'+base64.b64encode(png()).decode()}],
              'buffers':[{'byteLength':len(data),'uri':'data:application/octet-stream;base64,'+base64.b64encode(data).decode()}],'bufferViews':views,'accessors':accessors}
    if baked_reference:
        del document['materials'][0]['normalTexture']
    return document,{'cells':cells,'scale':.5,'image_width':2,'image_height':2,'normal_role':'linear RGB ignored alpha','scope':'independent literal expected directions, not GPU/image evidence'}


if __name__=='__main__':
    document,oracle=build()
    ((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest') / 'asymmetric-normal-uv-fixture.gltf').write_text(json.dumps(document,separators=(',',':'))+'\n')
    ((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest') / 'asymmetric-normal-uv-reference.json').write_text(json.dumps(oracle,indent=2)+'\n')
    ((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest') / 'asymmetric-baked-normal-reference.gltf').write_text(json.dumps(build(True)[0],separators=(',',':'))+'\n')
    # Every negative control must differ substantially on at least one cell;
    # simultaneous green+V inversion must also fail, not cancel.
    for name,mutate in [('green',lambda i,w: expected((TEXELS[i][0],255-TEXELS[i][1],TEXELS[i][2]),w)),
                       ('vflip',lambda i,w: expected(TEXELS[i^2],w)),
                       ('green_plus_vflip',lambda i,w: expected((TEXELS[i^2][0],255-TEXELS[i^2][1],TEXELS[i^2][2]),w)),
                       ('ignored_w',lambda i,w: expected(TEXELS[i],1)),
                       ('ignored_scale',lambda i,w: expected(TEXELS[i],w,scale=1))]:
        separation=max(max(abs(a-b) for a,b in zip(expected(TEXELS[i],w),mutate(i,w))) for i in range(4) for w in (-1,1))
        assert separation>.1,(name,separation)
        print(name,separation)
