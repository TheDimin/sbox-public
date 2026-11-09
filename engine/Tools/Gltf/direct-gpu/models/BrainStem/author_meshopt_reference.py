#!/usr/bin/env python3
"""Decode using the shipped meshoptimizer library, then independently evaluate glTF poses in Python.
This is not an independent codec oracle: the engine uses the same native codec.
"""
import ctypes, hashlib, importlib.util, json, pathlib, subprocess, sys
here=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('gltf',here.parent/'CesiumMan/author_reference.py'); r=importlib.util.module_from_spec(spec); spec.loader.exec_module(r)
corpus=pathlib.Path(sys.argv[1]).resolve(strict=True); library=pathlib.Path(sys.argv[2]).resolve(strict=True); output=pathlib.Path(sys.argv[3]); lib=ctypes.CDLL(str(library)); r.require(subprocess.check_output(['git','-C',str(corpus),'rev-parse','HEAD'],text=True).strip()==r.PIN,'wrong pin')
for n in ['meshopt_decodeVertexBuffer','meshopt_decodeIndexBuffer']:
 f=getattr(lib,n); f.argtypes=[ctypes.c_void_p,ctypes.c_size_t,ctypes.c_size_t,ctypes.c_void_p,ctypes.c_size_t]; f.restype=ctypes.c_int
for n in ['meshopt_decodeFilterOct','meshopt_decodeFilterQuat','meshopt_decodeFilterExp']:
 f=getattr(lib,n); f.argtypes=[ctypes.c_void_p,ctypes.c_size_t,ctypes.c_size_t]; f.restype=None
variants={}
for v in ('glTF-Meshopt','glTF-Meshopt-EXT'):
 path=corpus/'Models/BrainStem'/v/'BrainStem.gltf'; d=json.loads(path.read_bytes()); encoded=(path.parent/'BrainStem.bin').read_bytes(); s=object.__new__(r.Source); s.doc=d; s.cache={}; s.buffers=[]
 for view in d['bufferViews']:
  ext=next(iter(view['extensions'].values())); count=ext['count']; stride=ext['byteStride']; start=ext.get('byteOffset',0); data=encoded[start:start+ext['byteLength']]; target=ctypes.create_string_buffer(count*stride); src=ctypes.create_string_buffer(data)
  func=lib.meshopt_decodeVertexBuffer if ext['mode']=='ATTRIBUTES' else lib.meshopt_decodeIndexBuffer
  r.require(func(target,count,stride,src,len(data))==0,'native reference codec failed')
  filt=ext.get('filter','NONE'); filters={'OCTAHEDRAL':lib.meshopt_decodeFilterOct,'QUATERNION':lib.meshopt_decodeFilterQuat,'EXPONENTIAL':lib.meshopt_decodeFilterExp}
  if filt!='NONE': filters[filt](target,count,stride)
  s.buffers.append(target.raw); view['buffer']=len(s.buffers)-1; view['byteOffset']=0; view['byteLength']=count*stride; view.pop('extensions')
 primitives=[]
 for prim in d['meshes'][0]['primitives']:
  primitives.append({'indices':[x[0] for x in s.accessor(prim['indices'])['values']]})
 a=d['animations'][0]; times0=[x[0] for x in s.accessor(a['samplers'][0]['input'])['values']]; times=[('first_key',times0[0]),('between_keys',r.f32((times0[0]+times0[1])/2)),('middle_key',times0[len(times0)//2]),('last_key',times0[-1])]
 skin=d['skins'][0]; binds=[r.column_major(x) for x in s.accessor(skin['inverseBindMatrices'])['values']]; samples=[]
 for label,t in times:
  worlds=s.worlds(t); palette=[r.multiply(worlds[n],binds[i]) for i,n in enumerate(skin['joints'])]; pp=[]
  for prim in d['meshes'][0]['primitives']:
   attrs={n:s.accessor(i)['values'] for n,i in prim['attributes'].items()}; count=len(attrs['POSITION']); rows=[]
   for vertex in sorted(set((0,count//2,count-1))):
    blend=[[0.0]*4 for _ in range(4)]
    for joint,weight in zip(attrs['JOINTS_0'][vertex],attrs['WEIGHTS_0'][vertex]):
     for y in range(4):
      for x in range(4): blend[y][x]+=palette[joint][y][x]*weight
    point=attrs['POSITION'][vertex]; pos=[sum(blend[y][x]*point[x] for x in range(3))+blend[y][3] for y in range(3)]
    rows.append({'vertex':vertex,'position':pos,'normal':r.transform_normal(blend,attrs['NORMAL'][vertex])})
   pp.append(rows)
  samples.append({'label':label,'time':t,'primitives':pp})
 variants[v]={'primitives':primitives,'samples':samples}
payload={'pin':r.PIN,'codec_path':str(library),'codec_sha256':r.digest(library.read_bytes()),'author_sha256':r.digest(pathlib.Path(__file__).read_bytes()),'shared_arithmetic_sha256':r.digest((here.parent/'CesiumMan/author_reference.py').read_bytes()),'scope':'Native shared codec output + independent Python glTF pose/skin/normal arithmetic; not an independent codec proof.','variants':variants}; output.write_text(json.dumps(payload,separators=(',',':'),allow_nan=False)+'\n'); print(json.dumps({'sha256':r.digest(output.read_bytes()),'bytes':output.stat().st_size,'times':{v:[(q['label'],q['time']) for q in x['samples']] for v,x in variants.items()}}))
