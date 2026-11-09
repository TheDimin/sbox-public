#!/usr/bin/env python3
"""Read-only editor status, perf_memory schema and one memory snapshot.

Caller must hold the shared native-test lock and host guard admission.
"""
import hashlib
import json
import subprocess
from pathlib import Path

root=Path(__file__).resolve().parents[5]
marker=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
host=json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
editor=next(x for x in host['editors'] if x['pid']==marker['pid'])
assert marker['ready'] and editor['start']==marker['start'] and Path(editor['project']).resolve()==Path(marker['project']).resolve()
cli=str(root/'game/bin/managed/sbox-cli')
def call(name,args=None):
 command=[cli,'--connect',str(marker['pid']),'--call',name]
 if args is not None:command.append(json.dumps(args,separators=(',',':')))
 result=subprocess.run(command,cwd=root,capture_output=True,text=True,timeout=35,check=True)
 return json.loads(result.stdout)
status=call('editor_status')
assert Path(status['Paths']['ProjectRoot']).resolve()==Path(marker['project']).resolve().parent and not status['IsPlaying'] and not status['IsCompiling']
schema=call('search_tools',{'query':'perf_memory'})
schema_text=json.dumps(schema)
assert 'perf_memory' in schema_text and 'allocationSeconds' in schema_text, 'Memory tool/schema not verified'
memory=call('call_tool',{'name':'perf_memory','arguments':{'allocationSeconds':0,'limit':100}})
result={'pid':marker['pid'],'start':marker['start'],'project':marker['project'],'host':host,'editor_status':status,'perf_memory_schema':schema,'perf_memory_result':memory,'scope':'Read-only snapshot; no GC, cache clearing, process termination or scene modification.'}
path=root/'Temp/gltf-validation/direct-gpu/texture-benchmark/editor-memory-audit.json';path.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'receipt':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'perf_memory':memory},indent=2))
