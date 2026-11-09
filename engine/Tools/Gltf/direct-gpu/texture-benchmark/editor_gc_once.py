#!/usr/bin/env python3
"""One authorized GC.Collect, after idle verification, with read-only snapshots.

Caller holds the native-test lock and guarded admission; never retry automatically.
"""
import hashlib
import json
import subprocess
from pathlib import Path

root=Path(__file__).resolve().parents[5]
marker=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
host=lambda:json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard','status']))
before_host=host()
editor=next(x for x in before_host['editors'] if x['pid']==marker['pid'])
assert marker['ready'] and editor['start']==marker['start'] and Path(editor['project']).resolve()==Path(marker['project']).resolve()
cli=str(root/'game/bin/managed/sbox-cli')
def call(name,args=None):
 command=[cli,'--connect',str(marker['pid']),'--call',name]
 if args is not None:command.append(json.dumps(args,separators=(',',':')))
 result=subprocess.run(command,cwd=root,capture_output=True,text=True,timeout=35,check=True)
 return json.loads(result.stdout)
before_status=call('editor_status')
assert Path(before_status['Paths']['ProjectRoot']).resolve()==Path(marker['project']).resolve().parent and not before_status['IsPlaying'] and not before_status['IsCompiling'] and not before_status['SceneHasUnsavedChanges']
schema=call('search_tools',{'query':'perf_memory'})
assert 'perf_memory' in json.dumps(schema),'Memory tool schema unavailable'
before_memory=call('call_tool',{'name':'perf_memory','arguments':{}})
path=root/'Temp/gltf-validation/direct-gpu/texture-benchmark/editor-gc-once.json'
assert not path.exists(),'Refusing to overwrite existing GC receipt'
initial={'pid':marker['pid'],'start':marker['start'],'project':marker['project'],'before_host':before_host,'before_status':before_status,'memory_schema':schema,'before_memory':before_memory,'phase':'about_to_dispatch_once'}
path.write_text(json.dumps(initial,indent=2)+'\n')
code='''await MainThread.Wait(); var start = Application.FrameCount; System.GC.Collect(); for ( int i = 0; i < 30 && Application.FrameCount < start + 3; i++ ) { await Task.Delay( 100, cancellationToken ); await MainThread.Wait(); } return new { collected = true, startFrame = start, endFrame = Application.FrameCount };'''
try: collected=call('run_csharp',{'code':code})
except Exception as error:
 initial['phase']='dispatch_failed_or_uncertain_do_not_retry';initial['error']=str(error);path.write_text(json.dumps(initial,indent=2)+'\n');raise
initial['phase']='collection_returned';initial['collection_result']=collected;path.write_text(json.dumps(initial,indent=2)+'\n')
after_status=call('editor_status')
after_memory=call('call_tool',{'name':'perf_memory','arguments':{}})
after_host=host()
result={**initial,'phase':'complete','collection_result':collected,'after_status':after_status,'after_memory':after_memory,'after_host':after_host,'scope':'One plain GC.Collect equivalent to Editor/Debug/Full Garbage Collection; no finalizer wait, forced compaction, cache clearing, process termination, or system configuration changes.'}
path.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'receipt':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'collection_result':collected,'before_memory':before_memory,'after_memory':after_memory,'before_host':before_host['memory'],'after_host':after_host['memory']},indent=2))
