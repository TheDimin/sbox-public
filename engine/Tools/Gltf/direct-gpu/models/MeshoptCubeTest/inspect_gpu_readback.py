#!/usr/bin/env python3
"""Independent rest-pose inspection of actual GPU bytes; no Sandbox or codec evaluator."""
import argparse,base64,hashlib,json,math,pathlib,struct
p=argparse.ArgumentParser();p.add_argument('readback',type=pathlib.Path);p.add_argument('report',type=pathlib.Path);p.add_argument('--time',type=float,default=0);args=p.parse_args()
f=pathlib.Path(__file__).resolve().parent;raw=((pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MeshoptCubeTest') / 'reference.json').read_bytes()
assert hashlib.sha256(raw).hexdigest()=='bffc5439add7da08959b2a01ca93e748b274a3d3e26ce64f0d1b7b448d402219'
ref=json.loads(raw);rows=json.loads(args.readback.read_bytes());assert len(rows)==35
errors={'POSITION':0.0,'NORMAL':0.0,'COLOR':0.0,'TEXCOORD':0.0,'TANGENT_LENGTH':0.0,'TANGENT_NORMAL_DOT':0.0}
triangles={'exact':0,'cyclic':0,'reversed':0,'changed':0};differences=[];vertex_count=0;index_count=0;bad_tangents=[]
for row in rows:
 node=row['Node'];mesh=row['Mesh'];assert node==mesh and row['Primitive']==0 and row['IndexDataOrigin']=='gpu-readback'
 n=ref['nodes'][node]; prim=ref['meshes'][mesh]['primitives'][0]; attrs=prim['attributes'];data=base64.b64decode(row['Bytes']);stride=row['VertexStride'];fields={(x['Semantic'],x['SemanticIndex']):x for x in row['Fields']}
 count=ref['accessors'][attrs['POSITION']]['count'];assert row['VertexCount']==count and row['SourceVertices']==list(range(count)) and len(data)==count*stride
 def source(semantic,v):return ref['accessors'][attrs[semantic]]['values'][v]
 def gpu(semantic,v):
  field=fields[(semantic,0)];assert field['ComponentType']==5126;return struct.unpack_from('<'+'f'*field['Components'],data,v*stride+field['Offset'])
 for v in range(count):
  scale=n.get('scale',[1,1,1]);position=[x*s for x,s in zip(source('POSITION',v),scale)];t=max(0,min(args.time,2));angle=math.pi/2*min(t,2-t) if node>=14 and (node-14)%5==0 else 0
  def rotate(value):
   x,y,z=value;return [x*math.cos(angle)+z*math.sin(angle),y,-x*math.sin(angle)+z*math.cos(angle)]
  position=[x+shift for x,shift in zip(rotate(position),n['translation'])];position=[position[2]/.0254,position[0]/.0254,position[1]/.0254]
  normal=rotate([x/s for x,s in zip(source('NORMAL',v),scale)]);length=math.sqrt(sum(x*x for x in normal));normal=[normal[2]/length,normal[0]/length,normal[1]/length]
  color=source('COLOR_0',v) if 'COLOR_0' in attrs else [1,1,1,1];uv=source('TEXCOORD_0',v) if 'TEXCOORD_0' in attrs else [0,0]
  for semantic,expected in [('POSITION',position),('NORMAL',normal),('COLOR',color),('TEXCOORD',uv)]:
   actual=gpu(semantic,v);assert len(actual)==len(expected) and all(math.isfinite(x) for x in actual);errors[semantic]=max(errors[semantic],*(abs(a-b) for a,b in zip(actual,expected)))
  tangent=gpu('TANGENT',v);assert tangent[3]==1 and all(math.isfinite(x) for x in tangent)
  errors['TANGENT_LENGTH']=max(errors['TANGENT_LENGTH'],abs(math.sqrt(sum(x*x for x in tangent[:3]))-1));errors['TANGENT_NORMAL_DOT']=max(errors['TANGENT_NORMAL_DOT'],abs(sum(a*b for a,b in zip(tangent[:3],gpu('NORMAL',v)))))
  dot=sum(a*b for a,b in zip(tangent[:3],gpu('NORMAL',v)))
  if abs(dot)>3e-6:bad_tangents.append({'node':node,'vertex':v,'normal':gpu('NORMAL',v),'tangent':tangent,'dot':dot})
 vertex_count+=count
 expected=[int(x[0]) for x in ref['accessors'][prim['indices']]['values']];actual=row['Indices'];assert len(actual)==len(expected) and len(actual)%3==0
 for offset in range(0,len(actual),3):
  a=actual[offset:offset+3];b=expected[offset:offset+3]
  if a==b:kind='exact'
  elif a in [b[1:]+b[:1],b[2:]+b[:2]]:kind='cyclic'
  elif a in [[b[0],b[2],b[1]],[b[2],b[1],b[0]],[b[1],b[0],b[2]]]:kind='reversed'
  else:kind='changed'
  triangles[kind]+=1
  if kind!='exact':differences.append({'node':node,'triangle_ordinal':offset//3,'expected':b,'actual':a,'kind':kind})
 index_count+=len(actual)
report={'readback_path':str(args.readback.resolve()),'readback_sha256':hashlib.sha256(args.readback.read_bytes()).hexdigest(),'oracle_sha256':hashlib.sha256(raw).hexdigest(),'scope':'Independent source-time pose GPU byte parsing against Python-authored fallback values, analytic Y rotation, literal node scale/translation, source-to-engine basis and normalization. Triangle ordinal preserved; cyclic/reversed/changed classified separately.','vertices':vertex_count,'indices':index_count,'triangle_classification':triangles,'maximum_errors':errors,'differences':differences,'source_time':args.time,'bad_tangents':bad_tangents}
args.report.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:v for k,v in report.items() if k not in ('differences','bad_tangents')},indent=2));print('report_sha256',hashlib.sha256(args.report.read_bytes()).hexdigest())
assert vertex_count==640 and index_count==960
assert errors['POSITION']<=.0002 and errors['NORMAL']<=.000002 and errors['COLOR']<=.0000002 and errors['TEXCOORD']<=.0000002
assert errors['TANGENT_LENGTH']<=.000003 and errors['TANGENT_NORMAL_DOT']<=.000003
assert triangles['reversed']==0 and triangles['changed']==0
