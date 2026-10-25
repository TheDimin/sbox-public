import pathlib,json,statistics,re
ROOT=pathlib.Path(__file__).resolve().parent
fields=['ready_seconds','startup_cpu_seconds','startup_peak_rss_MiB','idle_cpu_percent_one_core','idle_median_VmRSS_MiB','idle_median_Pss_MiB']
records=[]
for name in ['A-1','B-1','B-2','A-2','A-3','B-3']:
 p=ROOT/name/'record.json'
 if not p.exists():continue
 d=json.loads(p.read_text());samples=json.loads((p.parent/'samples.json').read_text());d['idle_actual_wall_seconds']=samples[-1]['time']-samples[-46]['time'];d['idle_started_after_ready_seconds']=samples[-46]['elapsed']-d['ready_seconds'];d['shader_summaries']=re.findall(r'Shader checks took [^\n]+',(p.parent/'managed.log').read_text());d['asset_summaries']=re.findall(r'Asset imports: \d+ compile returned false',(p.parent/'managed.log').read_text())
 d['cpu_frame_work_ms']=d.get('perf_cpu',{}).get('CpuWork',{}).get('AvgMs')
 d['managed_heap_MiB']=d.get('perf_memory',{}).get('ManagedHeapMiB')
 records.append(d)
if not records and (ROOT/'measured-records.json').exists():records=json.loads((ROOT/'measured-records.json').read_text())
summary={'sample_count':len(records),'variants':{}}
for v in ['A','B']:
 rows=[d for d in records if d['variant']==v and 'failure' not in d]
 summary['variants'][v]={'count':len(rows),'metrics':{}}
 for f in fields+['cpu_frame_work_ms','managed_heap_MiB']:
  values=[r[f] for r in rows if r.get(f) is not None]
  if values:summary['variants'][v]['metrics'][f]={'median':statistics.median(values),'min':min(values),'max':max(values),'values':values}
summary['candidate_delta_percent']={}
for f,a in summary['variants']['A']['metrics'].items():
 b=summary['variants']['B']['metrics'].get(f)
 if b:summary['candidate_delta_percent'][f]=100*(b['median']/a['median']-1)
(ROOT/'summary.json').write_text(json.dumps(summary,indent=2))
(ROOT/'measured-records.json').write_text(json.dumps(records,indent=2))
print(json.dumps(summary,indent=2))
