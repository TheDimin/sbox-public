#!/usr/bin/env python3
"""Read-only manifest for one benchmark producer's generated cache entries."""
import getpass
import hashlib
import json
import os
import stat
import struct
from pathlib import Path

root=Path(__file__).resolve().parents[5]
asset=root/'Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb'
input_bytes=asset.read_bytes()
assert hashlib.sha256(input_bytes).hexdigest()=='bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007'
chunk_length,chunk_type=struct.unpack_from('<II',input_bytes,12)
assert chunk_type==0x4e4f534a
doc=json.loads(input_bytes[20:20+chunk_length])
binary_start=20+chunk_length
binary_length,binary_type=struct.unpack_from('<II',input_bytes,binary_start)
assert binary_type==0x004e4942
binary=memoryview(input_bytes)[binary_start+8:binary_start+8+binary_length]
assert len(doc['images'])==33
manifest=root/'Temp/gltf-validation/direct-gpu/texture-benchmark/selected-perf-stage/probe/probe-01-editor_direct_gpu_tests/receipt-01.json'
receipt=json.loads(manifest.read_text())
assert receipt['passed'] and receipt['engine_mvid']=='5832b742-ded3-46b8-94f2-1f62c65d5bd8' and receipt['gltf_mvid']=='075675b7-ad83-46c0-8a5d-3c53301f2d9c'
native=root/'game/bin/linuxsteamrt64/libSkiaSharp.so'
native_hash=hashlib.sha256(native.read_bytes()).hexdigest()
assert native_hash=='3ea5f4e22cf207e9ba49c28f712e40a62b0877788de78b507a1797f7aca176a9'
skia_mvid='3d3e32f1-d08a-447b-8fa3-22b24867d73f'
producer=f"{receipt['engine_mvid']}:{receipt['gltf_mvid']}:{skia_mvid}:{native_hash}"
directory=Path('/tmp')/('sbox-gltf-prepared-v1-'+hashlib.sha256(getpass.getuser().encode()).hexdigest())
assert directory.is_dir() and not directory.is_symlink() and directory.stat().st_uid==os.getuid()
roles=[]
for material in doc['materials']:
 pbr=material.get('pbrMetallicRoughness',{})
 for field,srgb in [(pbr.get('baseColorTexture'),True),(pbr.get('metallicRoughnessTexture'),False),(material.get('normalTexture'),False),(material.get('occlusionTexture'),False)]:
  if field is None:continue
  tex=doc['textures'][field['index']]; image_index=tex['source']; image=doc['images'][image_index]; view=doc['bufferViews'][image['bufferView']]
  assert image['mimeType']=='image/jpeg' and view.get('buffer',0)==0
  sampler=doc['samplers'][tex['sampler']] if 'sampler' in tex else {}
  mipmaps=sampler.get('minFilter',9987) not in (9728,9729)
  encoded=binary[view.get('byteOffset',0):view.get('byteOffset',0)+view['byteLength']]
  header=f"gltf-prepared-v1\0{producer}\0{image['mimeType']}\0{str(srgb)}\0{str(mipmaps)}\0".encode()
  digest=hashlib.sha256(header);digest.update(encoded);key=digest.hexdigest();roles.append({'key':key,'image':image_index,'srgb':srgb,'mipmaps':mipmaps,'source_sha256':hashlib.sha256(encoded).hexdigest()})
unique={x['key']:x for x in roles}; assert len(roles)==56 and len(unique)==33
rows=[]
for key,row in sorted(unique.items()):
 path=directory/(key+'.gltftex')
 if not path.exists():continue
 info=path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid==os.getuid() and not path.is_symlink()
 with path.open('rb') as stream:
  header=stream.read(48); assert header[:8]==b'GLTFTX01'; size=struct.unpack_from('<q',header,8)[0]
  assert size>0 and size<=256*1024*1024 and info.st_size==48+size
  digest=hashlib.sha256()
  for block in iter(lambda:stream.read(1024*1024),b''):digest.update(block)
  assert digest.digest()==header[16:48]
 rows.append({**row,'path':str(path),'size':info.st_size,'payload_sha256':digest.hexdigest(),'inode':info.st_ino,'mtime_ns':info.st_mtime_ns})
out={'scope':'read-only derived-key audit; no entries deleted','producer':producer,'input_sha256':hashlib.sha256(input_bytes).hexdigest(),'receipt_sha256':hashlib.sha256(manifest.read_bytes()).hexdigest(),'cache_directory':str(directory),'roles':len(roles),'derived_unique_keys':len(unique),'matched_entries':len(rows),'matched_bytes':sum(x['size'] for x in rows),'entries':rows}
target=root/'Temp/gltf-validation/direct-gpu/texture-benchmark/stale-cache-audit.json';target.write_text(json.dumps(out,indent=2)+'\n')
print(json.dumps({'manifest':str(target),'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'matched_entries':len(rows),'matched_bytes':out['matched_bytes']}))
