#!/usr/bin/env python3
"""Compute variant-local pose arithmetic from separately integrated Draco WASM outputs."""
import importlib.util,json,pathlib,subprocess,sys
here=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('gltf',here.parent/'CesiumMan/author_reference.py'); r=importlib.util.module_from_spec(spec); spec.loader.exec_module(r)
corpus=pathlib.Path(sys.argv[1]).resolve(strict=True); directory=pathlib.Path(sys.argv[2]); output=pathlib.Path(sys.argv[3]); r.require(subprocess.check_output(['git','-C',str(corpus),'rev-parse','HEAD'],text=True).strip()==r.PIN,'wrong pin')
s=r.Source(corpus,corpus/'Models/BrainStem/glTF-Draco/BrainStem.gltf'); d=s.doc; decoded=[]; source_hash=r.digest(s.path.read_bytes()); deps=[]
for p in range(59):
 path=directory/('draco-%02d.json'%p); raw=path.read_bytes(); x=json.loads(raw); r.require(x['pin']==r.PIN and x['source']['sha256']==source_hash,'mismatched Draco source'); r.require(x['weight_summary']['negative_components']==0,'negative weight'); decoded.append(x); deps.append(r.digest(raw))
keys=[x[0] for x in s.accessor(d['animations'][0]['samplers'][0]['input'])['values']]; times=[('first_key',keys[0]),('between_keys',r.f32((keys[0]+keys[1])/2)),('middle_key',keys[len(keys)//2]),('last_key',keys[-1])]; skin=d['skins'][0]; binds=[r.column_major(x) for x in s.accessor(skin['inverseBindMatrices'])['values']]; samples=[]
for label,t in times:
 worlds=s.worlds(t); palette=[r.multiply(worlds[n],binds[i]) for i,n in enumerate(skin['joints'])]; pp=[]
 for x in decoded:
  attrs=x['attributes']; count=x['vertex_count']; rows=[]
  def value(name,vertex):
   a=attrs[name]; n=a['components']; return a['values'][vertex*n:(vertex+1)*n]
  for vertex in sorted(set((0,count//2,count-1))):
   blend=[[0.0]*4 for _ in range(4)]
   for joint,weight in zip(value('JOINTS_0',vertex),value('WEIGHTS_0',vertex)):
    for y in range(4):
     for z in range(4): blend[y][z]+=palette[joint][y][z]*weight
   point=value('POSITION',vertex); pos=[sum(blend[y][z]*point[z] for z in range(3))+blend[y][3] for y in range(3)]
   rows.append({'vertex':vertex,'position':pos,'normal':r.transform_normal(blend,value('NORMAL',vertex))})
  pp.append(rows)
 samples.append({'label':label,'time':t,'primitives':pp})
payload={'pin':r.PIN,'source_sha256':source_hash,'decoder':decoded[0]['decoder'],'decoded_sha256':deps,'author_sha256':r.digest(pathlib.Path(__file__).read_bytes()),'shared_arithmetic_sha256':r.digest((here.parent/'CesiumMan/author_reference.py').read_bytes()),'variants':{'glTF-Draco':{'primitives':[{'indices':x['indices']} for x in decoded],'samples':samples}},'scope':'Separate official Draco JS/WASM integration, shared codec ancestry; independent Python glTF skin and pose arithmetic. No GPU or public result.'};output.write_text(json.dumps(payload,separators=(',',':'),allow_nan=False)+'\n');print(json.dumps({'sha256':r.digest(output.read_bytes()),'bytes':output.stat().st_size,'primitives':len(decoded),'times':times}))
