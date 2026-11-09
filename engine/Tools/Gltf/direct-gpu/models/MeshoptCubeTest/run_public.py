#!/usr/bin/env python3
# External flock and host codex-resource-guard must span this entire process.
import pathlib,json,hashlib,subprocess,sys,shutil,time
import argparse
parser=argparse.ArgumentParser(description='Run frozen model-owned tests only under the external native lock and resource guard.')
parser.add_argument('--mode',choices=('public','playback','diagnostic','pose-smoke','poses'),required=True)
args=parser.parse_args()
root=pathlib.Path(__file__).resolve().parents[6]
marker=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
guard=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
# Latest user AGENTS adds hard admission gates beyond helper estimates.
model_budget_bytes=2*1024**3
memory=guard['memory'];pressure=memory['pressure']
assert memory['used_bytes']+model_budget_bytes+guard.get('reserved_startup_bytes',0)<29_000_000_000,'Strict projected total must stay below 29 billion bytes'
assert memory['swap_free_bytes']*4>=memory['swap_total_bytes'],'Strict swap-free minimum is25percent'
assert all(pressure['some'][key]<5 and pressure['full'][key]<1 for key in ('avg10','avg60')),'Strict PSI gate'
e=next(x for x in guard['editors'] if x['pid']==marker['pid'])
assert e['start']==marker['start'] and pathlib.Path(e['project']).resolve()==pathlib.Path(marker['project']).resolve()
assert pathlib.Path('/proc/sys/kernel/random/boot_id').read_text().strip()==marker['bootId']
status=json.loads(subprocess.check_output([str(root/'game/bin/managed/sbox-cli'),'--connect',str(e['pid']),'--call','editor_status']))
assert status['Paths']['ProjectRoot']==str(pathlib.Path(marker['project']).parent)
assert not status['IsPlaying'] and not status['IsCompiling'] and status['LastCompileErrors']==0 and status['LastCompileSucceeded']
for row in marker['loaded']['modules']:
 assert hashlib.sha256(pathlib.Path(row['path']).read_bytes()).hexdigest()==row['sha256']
request=root/'research/gltf-validation/direct-gpu/models/MeshoptCubeTest'/(args.mode+'-request.json')
spec=json.loads(request.read_text())
for row in spec['sources']:assert hashlib.sha256((root/row['path']).read_bytes()).hexdigest()==row['sha256']
shutil.copyfile(request,root/'Temp/gltf-validation/direct-gpu/current-tests.json')
out=root/'Temp/gltf-validation/direct-gpu/MeshoptCubeTest'/(args.mode+'-run-'+time.strftime('%Y%m%d-%H%M%S'))
code=subprocess.call([sys.executable,str(root/'engine/Tools/Gltf/scripts/run_editor_probes.py'),'--pid',str(e['pid']),'--project',marker['project'],'--checkout',str(root),'--output',str(out),'--timeout','600','--confirmed-discovery',marker['discoveryPath'],'--confirmed-sha256',marker['discoverySha256'],'editor_direct_gpu_tests.csx'],cwd=root)
receipts=list(out.glob('probe-*/receipt-*.json'))
if not receipts:code=code or 2
if out.exists():(out/'host-admission-observation.json').write_text(json.dumps({'guard':guard,'minimum_additional_budget_bytes':model_budget_bytes,'strict_projected_total_bytes':memory['used_bytes']+model_budget_bytes+guard.get('reserved_startup_bytes',0)},indent=2)+'\n')
for receipt in receipts:
 j=json.loads(receipt.read_text())
 if not j.get('passed'):code=code or 2
 print(json.dumps({'receipt':str(receipt),'sha256':hashlib.sha256(receipt.read_bytes()).hexdigest(),'passed':j.get('passed'),'expected':j.get('expected_cases'),'pass':j.get('passed_cases'),'failed':j.get('failed_cases'),'incomplete':j.get('incomplete_cases'),'error':j.get('error'),'diagnostics':j.get('diagnostics'),'tests':j.get('tests')},indent=2))
print('OUTPUT',out,flush=True)
sys.exit(code)
