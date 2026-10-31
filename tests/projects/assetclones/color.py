#!/usr/bin/env python3
"""Independent Gamma/Linear color metadata controls; the shared launcher wraps children."""
import argparse, filecmp, os
from pathlib import Path
import shutil, subprocess, sys
import run
from probe_paths import reference_root, run_root, editor_path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('mode',choices=('editor','publish','unbound'));p.add_argument('--release',required=True);p.add_argument('--label',required=True);a=p.parse_args()
if Path(a.label).name!=a.label or a.label in ('','.','..'):p.error('label must be a single directory name')
os.environ['UNBOUND_PROBE_UNITY_VERSION']=a.release
work=run_root(run.PROBE,a.label)
if a.mode=='publish':
 for profile in ('Gamma','Linear'):
  first,second=(work/rep/(profile+'.jsonl') for rep in ('native1','native2'))
  if not filecmp.cmp(first,second,shallow=False):sys.exit(profile+': Native repeats differ')
  for rep in ('native1','native2'):
   log=(work/rep/(profile+'.log')).read_text(errors='replace')
   if 'NVIDIA' not in next((s for s in log.splitlines() if 'Renderer:' in s),''):sys.exit(profile+': NVIDIA renderer not verified')
  dest=reference_root(run.PROBE)/('Color'+profile+'.jsonl');dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(first,dest);print('published',dest)
 sys.exit(0)
name,server=run.display() if a.mode=='editor' else (os.environ.get('DISPLAY',':0'),None)
try:
 jobs=[]
 for profile,space in (('Gamma',0),('Linear',1)):
  for rep in (('native1','native2') if a.mode=='editor' else ('unbound',)):
   project=run.copy_project(work/'projects'/(profile+'-'+rep));settings=project/'ProjectSettings/ProjectSettings.asset';text=settings.read_text();assert 'm_ActiveColorSpace: 0' in text;settings.write_text(text.replace('m_ActiveColorSpace: 0','m_ActiveColorSpace: '+str(space)))
   out=work/rep/(profile+'.jsonl');out.parent.mkdir(parents=True,exist_ok=True);assert not out.exists()
   env=dict(os.environ,DISPLAY=name,ASSET_CLONE_MODE='color',ASSET_CLONE_OUT=str(out));log=work/rep/(profile+'.log')
   if a.mode=='editor':cmd=[str(run.LAUNCHER),'timeout','900','vglrun','-d','egl',str(editor_path()),'-batchmode','-projectPath',str(project),'-executeMethod','ProbeRunner.Record','-logFile',str(log)]
   else:cmd=[str(run.LAUNCHER),'timeout','600',os.environ['UNBOUND_CLI'],'run','-p',str(project),'--no-watch','--frames','10','--size','64x64']
   with log.open('w') as f:jobs.append(subprocess.Popen(cmd,env=env,stdout=f if a.mode=='unbound' else subprocess.DEVNULL,stderr=subprocess.STDOUT))
 codes=[j.wait() for j in jobs];print('exits',codes);sys.exit(int(any(codes)))
finally:
 if server:server.terminate()
