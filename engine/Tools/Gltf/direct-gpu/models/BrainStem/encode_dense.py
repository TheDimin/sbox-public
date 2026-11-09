#!/usr/bin/env python3
"""Make 10 Hz APNG from actual deterministic 0.1-second source-time seeks."""
import hashlib,json,pathlib,subprocess,sys
p=pathlib.Path(sys.argv[1]).resolve(strict=True); out=pathlib.Path(sys.argv[2]).resolve(); r=json.loads(p.read_text()); assert r['passed'] and not r['cleanup_errors']; v=r['measurements'][0]['value'][0]; assert v['error'] is None and len(v['captures'])==61
for i,f in enumerate(v['captures']):
 path=pathlib.Path(f['Path']).resolve(strict=True); assert path.name==f'sample_{i:04d}.png'; assert hashlib.sha256(path.read_bytes()).hexdigest()==f['Sha256']; assert abs(f['sequenceTime']-i*.1)<1e-5
folder=pathlib.Path(v['captures'][0]['Path']).parent; subprocess.run(['ffmpeg','-v','error','-framerate','10','-i',str(folder/'sample_%04d.png'),'-plays','0',str(out)],check=True); print(json.dumps({'path':str(out),'sha256':hashlib.sha256(out.read_bytes()).hexdigest(),'input_frames':61,'source_sample_interval_seconds':.1,'source_times':[0,6],'purpose':'Actual public GPU frames, deterministic source-time seeks; not observed real-time cadence.'}))
