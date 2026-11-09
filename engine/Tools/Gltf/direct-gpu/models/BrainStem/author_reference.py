#!/usr/bin/env python3
"""Independent core BrainStem reference; uses Python-only glTF arithmetic shared with CesiumMan.
Compressed geometry is deliberately not decoded and is never claimed by this oracle.
"""
import hashlib, importlib.util, json, pathlib, subprocess, sys
here = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('independent_gltf', here.parent/'CesiumMan/author_reference.py')
r = importlib.util.module_from_spec(spec); spec.loader.exec_module(r)
corpus = pathlib.Path(sys.argv[1]).resolve(strict=True)
out = pathlib.Path(sys.argv[2])
r.require(subprocess.check_output(['git','-C',str(corpus),'rev-parse','HEAD'],text=True).strip()==r.PIN, 'wrong pinned revision')
assets={}; variants={}
for v in ('glTF','glTF-Embedded','glTF-Binary','glTF-Draco','glTF-Meshopt','glTF-Meshopt-EXT'):
 p=corpus/'Models/BrainStem'/v/('BrainStem.glb' if v=='glTF-Binary' else 'BrainStem.gltf')
 if v.startswith('glTF-Meshopt'):
  d=json.loads(p.read_bytes()); deps={str((p.parent/b['uri']).relative_to(corpus)):r.digest((p.parent/b['uri']).read_bytes()) for b in d['buffers'] if 'uri' in b}; s=None
 else:
  s=r.Source(corpus,p); d=s.doc; deps=s.dependencies
 assets[v]={'relative':str(p.relative_to(corpus)),'sha256':r.digest(p.read_bytes()),'dependencies':deps,'extensions':d.get('extensionsRequired',[]),'primitives':len(d['meshes'][0]['primitives']),'channels':len(d['animations'][0]['channels']),'joints':d['skins'][0]['joints']}
 if v not in ('glTF','glTF-Embedded','glTF-Binary'): continue
 primitives=[]
 for pi, primitive in enumerate(d['meshes'][0]['primitives']):
  attrs={n:s.accessor(i) for n,i in primitive['attributes'].items()}
  indices=s.accessor(primitive['indices'])
  def desc(a): return {**{k:a[k] for k in ('offset','stride','component_type','components','count','normalized')},'raw_sha256':r.digest(a['raw'])}
  primitives.append({'attributes':{n:desc(a) for n,a in attrs.items()},'indices':desc(indices),'index_values':[x[0] for x in indices['values']]})
 times=[('rest',None),('first_key',0.0),('between_keys',r.f32((0.0+0.026666639372706413)/2)),('middle_key',s.accessor(d['animations'][0]['samplers'][0]['input'])['values'][654][0]),('last_key',s.accessor(d['animations'][0]['samplers'][0]['input'])['values'][-1][0])]
 samples=[]; skin=d['skins'][0]; binds=[r.column_major(x) for x in s.accessor(skin['inverseBindMatrices'])['values']]
 for label,t in times:
  worlds=s.worlds(t); palette=[r.multiply(worlds[n],binds[i]) for i,n in enumerate(skin['joints'])]; pp=[]
  for prim in d['meshes'][0]['primitives']:
   attrs={n:s.accessor(i)['values'] for n,i in prim['attributes'].items()}; count=len(attrs['POSITION']); selected=sorted(set((0,count//2,count-1))); rows=[]
   for vertex in selected:
    blend=[[0.0]*4 for _ in range(4)]
    for joint,weight in zip(attrs['JOINTS_0'][vertex],attrs['WEIGHTS_0'][vertex]):
     r.require(0<=joint<len(palette) and weight>=0,'invalid influence')
     for y in range(4):
      for x in range(4): blend[y][x]+=palette[joint][y][x]*weight
    point=attrs['POSITION'][vertex]; pos=[sum(blend[y][x]*point[x] for x in range(3))+blend[y][3] for y in range(3)]
    rows.append({'vertex':vertex,'position':pos,'normal':r.transform_normal(blend,attrs['NORMAL'][vertex])})
   pp.append(rows)
  samples.append({'label':label,'time':t,'worlds':[r.row_transform(w) for w in worlds],'primitives':pp})
 variants[v]={'primitives':primitives,'samples':samples}
payload={'pin':r.PIN,'assets':assets,'variants':variants,'author_sha256':r.digest(pathlib.Path(__file__).read_bytes()),'shared_arithmetic_sha256':r.digest((here.parent/'CesiumMan/author_reference.py').read_bytes()),'scope':'Independent source bytes and CPU glTF arithmetic only; compressed formats, runtime, GPU and appearance remain separate.'}
out.parent.mkdir(parents=True,exist_ok=True); out.write_text(json.dumps(payload,separators=(',',':'),allow_nan=False)+'\n'); print(json.dumps({'path':str(out),'sha256':r.digest(out.read_bytes()),'bytes':out.stat().st_size,'core_variants':len(variants),'samples_each':len(times),'primitives_each':len(primitives)}))
