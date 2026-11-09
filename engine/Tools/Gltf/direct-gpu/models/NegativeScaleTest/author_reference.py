"""Bounded stdlib raw-byte oracle. No s&box/glTF producer or evaluator imports."""
import base64, hashlib, json, math, struct
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
SOURCE=ROOT/'Temp/gltf-reference/glTF-Sample-Assets/Models/NegativeScaleTest'
REVISION='90d7ede14c7e280af263824604b427a1ca02cb66'

def identity(): return [[float(r==c) for c in range(4)] for r in range(4)]
def mul(a,b): return [[sum(a[r][k]*b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]
def det(a): return a[0][0]*(a[1][1]*a[2][2]-a[1][2]*a[2][1])-a[0][1]*(a[1][0]*a[2][2]-a[1][2]*a[2][0])+a[0][2]*(a[1][0]*a[2][1]-a[1][1]*a[2][0])
def local(n):
    if 'matrix' in n: return [[n['matrix'][c*4+r] for c in range(4)] for r in range(4)]
    x,y,z,w=n.get('rotation',[0,0,0,1]); s=n.get('scale',[1,1,1]); t=n.get('translation',[0,0,0]); a=identity()
    r=[[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]
    for i in range(3):
        for j in range(3): a[i][j]=r[i][j]*s[j]
        a[i][3]=t[i]
    return a

def worlds(j):
    result={}
    def walk(i,p):
        w=mul(p,local(j['nodes'][i])); result[i]=w
        for child in j['nodes'][i].get('children',[]): walk(child,w)
    for i in j['scenes'][j['scene']]['nodes']: walk(i,identity())
    return result

def point(p,a): return [sum(a[r][c]*p[c] for c in range(3))+a[r][3] for r in range(3)]
def direction(p,a): return [sum(a[r][c]*p[c] for c in range(3)) for r in range(3)]
def unit(p):
    length=math.sqrt(sum(x*x for x in p)); return [x/length for x in p]
def normal(p,a):
    d=det(a)
    if d==0: return None
    cof=[]
    for r in range(3):
        row=[]
        for c in range(3):
            rr=[x for x in range(3) if x!=r]; cc=[x for x in range(3) if x!=c]
            row.append((-1)**(r+c)*(a[rr[0]][cc[0]]*a[rr[1]][cc[1]]-a[rr[0]][cc[1]]*a[rr[1]][cc[0]])/d)
        cof.append(row)
    return unit(direction(p,cof))
def engine(p,direction_only=False):
    s=1 if direction_only else 1/.0254
    return [p[2]*s,p[0]*s,p[1]*s]
def winding(indices,d):
    result=indices[:]
    if d<0:
        for i in range(0,len(result),3): result[i+1],result[i+2]=result[i+2],result[i+1]
    return result

def load_source(path):
    b=path.read_bytes()
    if path.suffix=='.glb':
        magic,version,length=struct.unpack_from('<III',b); assert (magic,version,length)==(0x46546c67,2,len(b)); size,kind=struct.unpack_from('<II',b,12); assert kind==0x4e4f534a
        j=json.loads(b[20:20+size]); off=20+size; n,kind=struct.unpack_from('<II',b,off); assert kind==0x004e4942
        return j,b[off+8:off+8+n]
    j=json.loads(b); return j,(path.parent/j['buffers'][0]['uri']).read_bytes()

def accessor(j,raw,index):
    a=j['accessors'][index]; v=j['bufferViews'][a['bufferView']]; fmt={5123:'H',5126:'f'}[a['componentType']]; width=struct.calcsize(fmt); count={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]; stride=v.get('byteStride',count*width); offset=v.get('byteOffset',0)+a.get('byteOffset',0)
    return {'component_type':a['componentType'],'components':count,'count':a['count'],'view_offset':v.get('byteOffset',0),'accessor_offset':a.get('byteOffset',0),'stride':stride,'view_length':v['byteLength'],'values':[list(struct.unpack_from('<'+fmt*count,raw,offset+i*stride)) for i in range(a['count'])]}

def build_reference(j,raw):
    accessors=[accessor(j,raw,i) for i in range(len(j['accessors']))]; rows=[]; ws=worlds(j)
    for node,w in sorted(ws.items()):
        n=j['nodes'][node]
        if 'mesh' not in n: continue
        for primitive,p in enumerate(j['meshes'][n['mesh']]['primitives']):
            attrs=p['attributes']; points=[engine(point(v,w)) for v in accessors[attrs['POSITION']]['values']]; normals=None if det(w)==0 else [engine(normal(v,w),True) for v in accessors[attrs['NORMAL']]['values']]; tangents=None
            if 'TANGENT' in attrs and normals is not None:
                tangents=[]
                for i,v in enumerate(accessors[attrs['TANGENT']]['values']):
                    t=engine(direction(v[:3],w),True); nrm=normals[i]; dot=sum(a*b for a,b in zip(t,nrm)); t=unit([a-b*dot for a,b in zip(t,nrm)])
                    tangents.append(t+[v[3]*(-1 if det(w)<0 else 1)])
            rows.append({'node':node,'mesh':n['mesh'],'primitive':primitive,'material':p.get('material'),'world_column_major':[w[r][c] for c in range(4) for r in range(4)],'determinant':det(w),'attributes':attrs,'indices':[v[0] for v in accessors[p['indices']]['values']],'positions':points,'normals':normals,'tangents':tangents})
    points=[p for row in rows for p in row['positions']]
    return {'accessors':accessors,'rows':rows,'bounds_min':[min(p[c] for p in points) for c in range(3)],'bounds_max':[max(p[c] for p in points) for c in range(3)]}

def fixture():
    # Parent nonuniform scale followed by child rotation yields a non-TRS world matrix.
    # Each authored local remains valid decomposable glTF TRS.
    t=unit([1,1,0]); n=unit([1,-1,1]); b=[n[1]*t[2]-n[2]*t[1],n[2]*t[0]-n[0]*t[2],n[0]*t[1]-n[1]*t[0]]
    blocks=[(5126,'VEC3',[[0,0,0],t,b]),(5126,'VEC3',[n]*3),(5126,'VEC4',[t+[1]]*3),(5123,'SCALAR',[[0],[1],[2]]),(5126,'SCALAR',[[0],[1],[2],[3]]),(5126,'VEC3',[[-1,2,.75],[0,2,.75],[0,0,0],[-1,2,.75]])]
    raw=bytearray(); views=[]; accessors=[]
    for component,kind,values in blocks:
        while len(raw)%4: raw.append(0)
        start=len(raw); fmt='H' if component==5123 else 'f'
        for value in values: raw.extend(struct.pack('<'+fmt*len(value),*value))
        views.append({'buffer':0,'byteOffset':start,'byteLength':len(raw)-start}); a={'bufferView':len(views)-1,'componentType':component,'type':kind,'count':len(values)}
        if len(accessors)==0: a.update(min=[min(v[c] for v in values) for c in range(3)],max=[max(v[c] for v in values) for c in range(3)])
        if len(accessors)==4: a.update(min=[0],max=[3])
        accessors.append(a)
    j={'asset':{'version':'2.0','generator':'Independent NegativeScaleTest affine/singular fixture'},'scene':0,'scenes':[{'nodes':[0]}],'nodes':[{'translation':[3,-5,7],'children':[1]},{'scale':[1.5,.75,2],'rotation':[0,math.sin(math.pi/8),0,math.cos(math.pi/8)],'children':[2]},{'mesh':0,'rotation':[0,0,math.sin(math.pi/8),math.cos(math.pi/8)],'scale':[-1,2,.75]}],'meshes':[{'primitives':[{'attributes':{'POSITION':0,'NORMAL':1,'TANGENT':2},'indices':3,'material':0}]}],'materials':[{'doubleSided':False,'pbrMetallicRoughness':{'baseColorFactor':[.2,.8,.3,1],'metallicFactor':0,'roughnessFactor':1}}],'buffers':[{'byteLength':len(raw),'uri':'data:application/octet-stream;base64,'+base64.b64encode(raw).decode()}],'bufferViews':views,'accessors':accessors,'animations':[{'samplers':[{'input':4,'output':5,'interpolation':'LINEAR'}],'channels':[{'sampler':0,'target':{'node':2,'path':'scale'}}]}]}
    return j,bytes(raw)

def main():
    doc,raw=load_source(SOURCE/'glTF/NegativeScaleTest.gltf'); ref=build_reference(doc,raw); ref['revision']=REVISION
    ref['files']={str(p.relative_to(SOURCE)):{'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'length':p.stat().st_size} for p in sorted(SOURCE.rglob('*')) if p.is_file() and p.suffix in ('.gltf','.glb','.bin','.png')}
    fj,fb=fixture(); ((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/NegativeScaleTest') / 'affine-tangent-fixture.gltf').write_text(json.dumps(fj,separators=(',',':'))+'\n')
    ref['fixture']={str(time):build_reference(dict(fj,nodes=[*fj['nodes'][:2],dict(fj['nodes'][2],scale=scale)]),fb) for time,scale in [(0,[-1,2,.75]),(1,[0,2,.75]),(2,[0,0,0]),(3,[-1,2,.75])]}
    ((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/NegativeScaleTest') / 'reference.json').write_text(json.dumps(ref,separators=(',',':'))+'\n')
    print(json.dumps({'rows':len(ref['rows']),'accessors':len(ref['accessors']),'negative_nodes':[r['node'] for r in ref['rows'] if r['determinant']<0],'bounds':[ref['bounds_min'],ref['bounds_max']]}))
if __name__=='__main__': main()
