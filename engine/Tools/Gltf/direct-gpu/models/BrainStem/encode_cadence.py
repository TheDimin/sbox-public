#!/usr/bin/env python3
"""Encode the actual timed PNG captures as an APNG; no synthetic pose interpolation."""
import hashlib,json,pathlib,subprocess,sys
receipt=pathlib.Path(sys.argv[1]).resolve(strict=True); output=pathlib.Path(sys.argv[2]).resolve(); data=json.loads(receipt.read_text()); assert data['passed'] and not data['cleanup_errors']; rows=data['measurements'][0]['value']; assert len(rows)==1 and rows[0]['error'] is None
frames=rows[0]['captures']; assert len(frames)>2
manifest=output.with_suffix('.concat.txt'); lines=[]
for i,frame in enumerate(frames):
 p=pathlib.Path(frame['Path']).resolve(strict=True); assert hashlib.sha256(p.read_bytes()).hexdigest()==frame['Sha256']; assert "'" not in str(p) and '\n' not in str(p); lines.append("file '"+str(p)+"'")
 dt=frames[i+1]['ObservedWallclockSeconds']-frame['ObservedWallclockSeconds'] if i+1<len(frames) else frames[-1]['ObservedWallclockSeconds']-frames[-2]['ObservedWallclockSeconds']; assert dt>0; lines.append('duration '+format(dt,'.9f'))
lines.append("file '"+str(pathlib.Path(frames[-1]['Path']).resolve())+"'"); manifest.write_text('\n'.join(lines)+'\n'); subprocess.run(['ffmpeg','-v','error','-f','concat','-safe','0','-i',str(manifest),'-fps_mode','vfr','-plays','0',str(output)],check=True); info=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_entries','stream=width,height,nb_frames:format=duration','-of','json',str(output)])); print(json.dumps({'path':str(output),'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'input_frames':len(frames),'first_wall_seconds':frames[0]['ObservedWallclockSeconds'],'last_wall_seconds':frames[-1]['ObservedWallclockSeconds'],'ffprobe':info,'scope':'APNG re-encodes actual observed capture frames using recorded wall-clock gaps, subject to ffmpeg timestamp quantization; final hold duplicates the final frame. The receipt retains exact observations.'}))
