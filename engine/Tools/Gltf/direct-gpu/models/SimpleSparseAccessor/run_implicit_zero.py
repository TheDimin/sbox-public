#!/usr/bin/env python3
# Run under the native flock and host resource guard. Do not detach.
import pathlib,json,hashlib,subprocess,sys,shutil,time
root=pathlib.Path(__file__).resolve().parents[6]
m=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
g=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
e=next(x for x in g['editors'] if x['pid']==m['pid'])
assert e['start']==m['start'] and pathlib.Path(e['project']).resolve()==pathlib.Path(m['project']).resolve()
assert pathlib.Path('/proc/sys/kernel/random/boot_id').read_text().strip()==m['bootId']
s=json.loads(subprocess.check_output([str(root/'game/bin/managed/sbox-cli'),'--connect',str(e['pid']),'--call','editor_status']))
assert s['Paths']['ProjectRoot']==str(pathlib.Path(m['project']).parent) and not s['IsPlaying'] and not s['IsCompiling'] and s['LastCompileSucceeded'] and s['LastCompileErrors']==0
req=root/'research/gltf-validation/direct-gpu/models/SimpleSparseAccessor/implicit-zero-request.json'; j=json.loads(req.read_text())
for x in j['sources']: assert hashlib.sha256((root/x['path']).read_bytes()).hexdigest()==x['sha256']
for x in m['loaded']['modules']: assert hashlib.sha256(pathlib.Path(x['path']).read_bytes()).hexdigest()==x['sha256']
assert j['engine_sha256']==next(x['sha256'] for x in m['loaded']['modules'] if x['name']=='Sandbox.Engine')
shutil.copyfile(req,root/'Temp/gltf-validation/direct-gpu/current-tests.json')
out=root/'Temp/gltf-validation/direct-gpu/SimpleSparseAccessor'/('implicit-zero-run-'+time.strftime('%Y%m%d-%H%M%S'))
code=subprocess.call([sys.executable,str(root/'engine/Tools/Gltf/scripts/run_editor_probes.py'),'editor_direct_gpu_tests.csx','--pid',str(e['pid']),'--project',m['project'],'--output',str(out),'--confirmed-discovery',m['discoveryPath'],'--confirmed-sha256',m['discoverySha256'],'--timeout','300'],cwd=root)
receipts=list(out.glob('probe-*/receipt-*.json'))
if not receipts: code=code or 2
for p in receipts:
 j=json.loads(p.read_text()); print(json.dumps({'receipt':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'passed':j.get('passed'),'expected':j.get('expected_cases'),'passed_cases':j.get('passed_cases'),'failed_cases':j.get('failed_cases'),'incomplete_cases':j.get('incomplete_cases'),'error':j.get('error')},indent=2),flush=True)
 if not j.get('passed'):code=code or 2
sys.exit(code)
