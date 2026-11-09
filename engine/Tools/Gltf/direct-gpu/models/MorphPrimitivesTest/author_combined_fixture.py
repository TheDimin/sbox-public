#!/usr/bin/env python3
"""Author a small derived glTF fixture; no Khronos asset bytes are changed."""
import base64, json, struct
from pathlib import Path
blob=bytearray(b'\xa5'*16)
views=[]; accessors=[]
def stream(values, fmt, typ, components, stride, offset, target=True, normalized=False):
    start=len(blob); size=stride*len(values); blob.extend(b'\xcd'*size)
    for i,row in enumerate(values): struct.pack_into('<'+fmt*components,blob,start+i*stride+offset,*row)
    view={'buffer':0,'byteOffset':start,'byteLength':size,'byteStride':stride}
    if target: view['target']=34962
    views.append(view); a={'bufferView':len(views)-1,'byteOffset':offset,'componentType':{'f':5126,'B':5121}[fmt], 'count':len(values),'type':typ}
    if normalized:a['normalized']=True
    accessors.append(a); return len(accessors)-1
pos=stream([(0,0,0),(1,0,0),(0,1,0)],'f','VEC3',3,20,4); accessors[pos].update(min=[0,0,0],max=[1,1,0])
n=stream([(0,0,1)]*3,'f','VEC3',3,20,4)
t=stream([(1,0,0,-1)]*3,'f','VEC4',4,24,4)
j=stream([(0,0,0,0)]*3,'B','VEC4',4,8,4)
w=stream([(255,0,0,0)]*3,'B','VEC4',4,8,4,normalized=True)
targets=[]
for dp,dn,dt in [((1,0,0),(1,0,0),(0,1,0)),((0,1,0),(0,1,0),(0,0,1))]:
    target={s:stream([v]*3,'f','VEC3',3,20,4) for s,v in [('POSITION',dp),('NORMAL',dn),('TANGENT',dt)]}
    accessors[target['POSITION']].update(min=list(dp),max=list(dp))
    targets.append(target)
doc={'asset':{'version':'2.0','generator':'derived combined morph skin test'},'scene':0,'scenes':[{'nodes':[0,1]}],
 'nodes':[{'mesh':0,'skin':0},{'rotation':[0,0,0.7071067811865476,0.7071067811865476]}],
 'skins':[{'joints':[1]}], 'meshes':[{'weights':[0.5,-0.25],'primitives':[{'attributes':{'POSITION':pos,'NORMAL':n,'TANGENT':t,'JOINTS_0':j,'WEIGHTS_0':w},'targets':targets}]}],
 'buffers':[{'byteLength':len(blob),'uri':'data:application/octet-stream;base64,'+base64.b64encode(blob).decode()}], 'bufferViews':views,'accessors':accessors}
(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MorphPrimitivesTest') / 'combined-morph-skin.gltf'.write_text(json.dumps(doc,indent=2)+'\n')
