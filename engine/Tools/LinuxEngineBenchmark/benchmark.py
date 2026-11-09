import os,pathlib,subprocess,time,json,signal,shutil,statistics,datetime
BASE=pathlib.Path('/home/damian/.codex/worktrees')
OUT=BASE/'linux-engine-validation/sbox-public/validation-results/ab-comparison'
COMMON=BASE/'linux-engine-validation/Survive-profile'
HOLD=COMMON.with_name('Survive-profile-ab-hold')
CLI=BASE/'linux-engine-validation/sbox-public/game/bin/managed/sbox-cli'
HZ=os.sysconf('SC_CLK_TCK')

def measure(pid):
 s=pathlib.Path(f'/proc/{pid}')
 stat=(s/'stat').read_text().rsplit(')',1)[1].split()
 d={'loadavg':pathlib.Path('/proc/loadavg').read_text().strip(),'time':time.monotonic(),'cpu_seconds':(int(stat[11])+int(stat[12]))/HZ}
 for line in (s/'status').read_text().splitlines():
  if line.split(':')[0] in ['VmRSS','VmHWM','RssAnon']:d[line.split(':')[0]+'_KiB']=int(line.split()[1])
 try:
  for line in (s/'smaps_rollup').read_text().splitlines():
   if line.startswith(('Pss:','Private_Clean:','Private_Dirty:')):d[line.split(':')[0]+'_KiB']=int(line.split()[1])
 except (PermissionError,FileNotFoundError):pass
 return d

def run(variant,name):
 root=BASE/('linux-engine-baseline' if variant=='A' else 'linux-engine-comparison')/'sbox-public'
 game=root/'game'; storage=root.parent/'Survive-ab'; folder=OUT/name;folder.mkdir(exist_ok=True)
 storage.rename(COMMON)
 env=os.environ.copy();env.update(XDG_RUNTIME_DIR='/tmp/sbox-ab-display/runtime',WAYLAND_DISPLAY='sbox-ab-wayland',XAUTHORITY='',DISPLAY=':1',FACEPUNCH_ENGINE=str(game),LD_LIBRARY_PATH=str(game/'bin/linuxsteamrt64')+':'+env.get('LD_LIBRARY_PATH',''),__NV_PRIME_RENDER_OFFLOAD='1',__GLX_VENDOR_LIBRARY_NAME='nvidia',__VK_LAYER_NV_optimus='NVIDIA_only',VK_DRIVER_FILES='/usr/share/vulkan/icd.d/nvidia_icd.x86_64.json',SDL_VIDEO_X11_EXTERNAL_WINDOW_INPUT='1',SDL_VIDEO_X11_XINPUT2='0',QT_QPA_PLATFORM='xcb',LD_PRELOAD=str(game/'bin/linuxsteamrt64/libHarfBuzzSharp.so'))
 for p in (game/'data').rglob('startup-profile.*'):p.unlink()
 samples=[];rec={'variant':variant,'name':name,'started_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'revision':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()}
 child=None
 def cli(tool):
  p=subprocess.run([str(CLI),'--connect',str(child.pid),'--call',tool,'--request-timeout','15'],env=env,cwd=COMMON,capture_output=True,text=True,timeout=25)
  if p.returncode:raise RuntimeError(p.stderr or p.stdout)
  return json.loads(p.stdout)
 def sample():
  if child.poll() is not None:raise RuntimeError('Editor exited '+str(child.returncode))
  s=measure(child.pid);s['elapsed']=s['time']-start;samples.append(s);return s
 try:
  with (folder/'stdout.log').open('wb') as log:
   start=time.monotonic();child=subprocess.Popen([str(game/'sbox-dev'),'-project',str(COMMON/'survive.sbproj'),'-startup-profile'],cwd=game,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
   rec['pid']=child.pid;print('START',name,child.pid,flush=True)
   deadline=start+900
   while time.monotonic()<deadline:
    sample();profiles=list((game/'data').rglob('startup-profile.json'))
    if profiles:
     try:
      profile=json.loads(profiles[0].read_text());state=cli('editor_status')
      if state.get('IsCompiling',False):time.sleep(1);continue
      if state.get('LastCompileSucceeded') is False or state.get('LastCompileErrors',0):raise ValueError('Project compilation failed')
      assert pathlib.Path(state['Paths']['Engine'])==game, state
      assert pathlib.Path(state['Paths']['ProjectRoot'])==COMMON, state
      assert state['ActiveScenePath']=='scenes/minimal.scene' and not state['IsPlaying'], state
      rec['editor_status']=state;rec['profile']=profile;shutil.copy2(profiles[0],folder/'startup-profile.json');break
     except (subprocess.TimeoutExpired,RuntimeError,json.JSONDecodeError) as e:print('WAIT',name,str(e)[:160],flush=True)
    time.sleep(1)
   else:raise TimeoutError('Startup exceeded 900 seconds')
   ready=sample();rec['ready_seconds']=ready['elapsed'];rec['startup_cpu_seconds']=ready['cpu_seconds'];rec['startup_peak_rss_MiB']=max(x.get('VmRSS_KiB',0) for x in samples)/1024
   print('READY',name,round(rec['ready_seconds'],2),flush=True)
   for _ in range(15):time.sleep(1);sample()
   begin=sample();idle=[]
   for _ in range(45):time.sleep(1);idle.append(sample())
   end=idle[-1];rec['idle_cpu_percent_one_core']=100*(end['cpu_seconds']-begin['cpu_seconds'])/(end['time']-begin['time'])
   for field in ['VmRSS','RssAnon','Pss','Private_Clean','Private_Dirty']:
    values=[x[field+'_KiB']/1024 for x in idle if field+'_KiB' in x]
    if values:rec['idle_median_'+field+'_MiB']=statistics.median(values)
   for tool in ['perf_cpu','perf_memory']:
    try:rec[tool]=cli(tool)
    except Exception as e:rec[tool+'_error']=repr(e)
 except Exception as e:rec['failure']=repr(e)
 finally:
  if child is not None:
   if child.poll() is None:
    child.terminate()
    try:child.wait(timeout=15)
    except subprocess.TimeoutExpired:os.killpg(child.pid,signal.SIGKILL);child.wait(timeout=10)
   rec['cleanup_exit']=child.returncode
  logfile=game/'logs/sbox-dev.log'
  if logfile.exists():shutil.copy2(logfile,folder/'managed.log')
  COMMON.rename(storage)
  (folder/'samples.json').write_text(json.dumps(samples,indent=2));(folder/'record.json').write_text(json.dumps(rec,indent=2))
  print('END',name,json.dumps({k:v for k,v in rec.items() if k not in ['profile','editor_status','perf_cpu','perf_memory']}),flush=True)
 if rec.get('failure'):raise RuntimeError(rec['failure'])
 time.sleep(5)

if __name__=='__main__':
 def interrupted(signum, frame):raise KeyboardInterrupt('Benchmark interrupted')
 signal.signal(signal.SIGTERM,interrupted)
 if HOLD.exists():raise RuntimeError('Holdout exists; restore prior interrupted state first')
 COMMON.rename(HOLD)
 try:
  for variant,name in [('A','prepare-A'),('B','prepare-B'),('A','A-1'),('B','B-1'),('B','B-2'),('A','A-2'),('A','A-3'),('B','B-3')]:run(variant,name)
 finally:
  if not COMMON.exists():HOLD.rename(COMMON)
