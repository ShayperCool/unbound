#!/usr/bin/env python3
"""Explicit private CLI/managed/native paths; unchanged existing comparators."""
from pathlib import Path
import os, shutil, subprocess, sys
root=Path(__file__).resolve().parents[3];f=Path(__file__).resolve().parent
out=Path(os.environ['MATERIAL_COLOR_REGRESS_OUT']);out.mkdir(parents=True,exist_ok=True)
cli=os.environ['MATERIAL_COLOR_TRANSFER_CLI'];heavy=str(Path.home()/'.cache/compat/with-heavy-job')
projects=root/'tests/projects'
def copy(project,space):
 p=f/'Library'/('Regression'+project+space)
 for part in ['Assets','Packages','ProjectSettings']:shutil.copytree(projects/project/part,p/part,dirs_exist_ok=True)
 if space=='Gamma':
  s=p/'ProjectSettings/ProjectSettings.asset';s.write_text(s.read_text().replace('m_ActiveColorSpace: 1','m_ActiveColorSpace: 0'))
 return p
jobs=[('uitk','UITK_PROBE_OUT','uitk',['--frames','35','--size','960x540']),('graphics','GRAPHICS_PROBE_OUT','graphics',['--frames','30']),('resources','RESOURCES_PROBE_OUT','resources',['--frames','2000']),('builtinrp','BUILTIN_PROBE_OUT','builtinrp',['--frames','30']),('builtinfx','BUILTINFX_PROBE_OUT','builtinfx',['--frames','2000'])]
for space in ['Linear','Gamma']:
 for project,key,var,frames in [('uirectclipping','rectclip','RECT_CLIP_OUT','24'),('uicolors','colors','UI_COLORS_OUT','8')]:
  jobs.append((key+'-'+space.lower(),var,copy(project,space),['--frames',frames,'--size','128x128']))
 jobs.append(('camera-focused-'+space.lower(),'CAMERA_MSAA_OUT',copy('cameramsaa',space),['--frames','64','--size','128x128']))
jobs.append(('rectclip-msaa','RECT_CLIP_OUT',copy('uirectclipping','Linear'),['--frames','24','--size','128x128']))
for key,var,project,args in jobs:
 env=dict(os.environ);env[var]=str(out/key/('unbound.jsonl' if key=='uitk' else ''))
 if key=='rectclip-msaa':env['RECT_CLIP_MSAA']='1'
 if key.startswith('camera-focused'):env['CAMERA_MSAA_FOCUSED']='1'
 p=Path(project) if isinstance(project,Path) else projects/project
 with (out/(key+'.log')).open('w') as log:
  result=subprocess.run([heavy,'timeout','600',cli,'run','-p',str(p),'--no-watch',*args],env=env,stdout=log,stderr=subprocess.STDOUT)
 print(key,'exit',result.returncode,flush=True)
 if result.returncode:raise SystemExit(result.returncode)
