#!/usr/bin/env python3
"""Selective, reviewed stale benchmark-cache cleanup; caller holds publication lock."""
import hashlib
import json
import os
import stat
import struct
import subprocess
from pathlib import Path

root=Path(__file__).resolve().parents[5]
manifest_path=root/'Temp/gltf-validation/direct-gpu/texture-benchmark/stale-cache-audit.json'
raw=manifest_path.read_bytes()
expected='bfe96e2afdbc51f1e305fc8077225da0496c4edb01305b0e3ebcfb5c8de501bf'
assert hashlib.sha256(raw).hexdigest()==expected, 'reviewed manifest changed'
manifest=json.loads(raw); assert manifest['matched_entries']==33 and manifest['matched_bytes']==738201288
asset=root/'Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb'
assert hashlib.sha256(asset.read_bytes()).hexdigest()==manifest['input_sha256']=='bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007'
marker=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
host=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
editor=next(x for x in host['editors'] if x['pid']==marker['pid'])
assert marker['ready'] and editor['start']==marker['start'] and Path(editor['project']).resolve()==Path(marker['project']).resolve()
engine=next(x for x in marker['loaded']['modules'] if x['name']=='Sandbox.Engine')
assert engine['loadedMvid']==engine['diskMvid'] and engine['loadedMvid']!='5832b742-ded3-46b8-94f2-1f62c65d5bd8'
directory=Path(manifest['cache_directory'])
assert directory.is_absolute() and not directory.is_symlink() and directory.resolve()==directory and directory.stat().st_uid==os.getuid()
dirfd=os.open(directory,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
deleted=[]; skipped=[]
try:
 for row in manifest['entries']:
  path=Path(row['path']); name=path.name
  assert path.parent==directory and name==row['key']+'.gltftex' and len(row['key'])==64
  try:
   fd=os.open(name,os.O_RDONLY|os.O_NOFOLLOW,dir_fd=dirfd)
   with os.fdopen(fd,'rb') as stream:
    st=os.fstat(stream.fileno())
    assert stat.S_ISREG(st.st_mode) and st.st_uid==os.getuid() and st.st_ino==row['inode'] and st.st_mtime_ns==row['mtime_ns'] and st.st_size==row['size']
    header=stream.read(48); assert header[:8]==b'GLTFTX01' and struct.unpack_from('<q',header,8)[0]+48==st.st_size
    digest=hashlib.sha256()
    for block in iter(lambda:stream.read(1024*1024),b''):digest.update(block)
    assert digest.digest()==header[16:48] and digest.hexdigest()==row['payload_sha256']
    check=os.stat(name,dir_fd=dirfd,follow_symlinks=False)
    assert check.st_ino==st.st_ino and check.st_mtime_ns==st.st_mtime_ns and check.st_size==st.st_size and stat.S_ISREG(check.st_mode)
    os.unlink(name,dir_fd=dirfd);deleted.append({'key':row['key'],'bytes':st.st_size})
  except (OSError,AssertionError,ValueError) as error: skipped.append({'key':row['key'],'reason':str(error)})
finally:os.close(dirfd)
remaining=[p for p in directory.glob('*.gltftex') if p.is_file() and not p.is_symlink()]
receipt={'reviewed_manifest_sha256':expected,'deleted':deleted,'skipped':skipped,'reclaimed_bytes':sum(x['bytes'] for x in deleted),'remaining_entries':len(remaining),'remaining_bytes':sum(p.stat().st_size for p in remaining),'cap_bytes':2*1024**3,'scope':'Only reviewed superseded producer keys; publication lock held by caller, changed files skipped; lock and source assets preserved.'}
out=root/'Temp/gltf-validation/direct-gpu/texture-benchmark/stale-cache-prune-receipt.json';out.write_text(json.dumps(receipt,indent=2)+'\n')
print(json.dumps({'receipt':str(out),'sha256':hashlib.sha256(out.read_bytes()).hexdigest(),'deleted':len(deleted),'skipped':len(skipped),'reclaimed_bytes':receipt['reclaimed_bytes'],'remaining_bytes':receipt['remaining_bytes']}))
raise SystemExit(0 if not skipped else 1)
