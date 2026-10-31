#!/usr/bin/env python3
"""Replay root's independently measured BoxCollider skin transitions with private outputs."""
import argparse, os, shutil, subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parent; REPO=ROOT.parents[2]; WORK=Path(os.environ['MOTION_WORK'])
p=argparse.ArgumentParser();p.add_argument('label');p.add_argument('--baseline',action='store_true');a=p.parse_args()
project=WORK/'project-skin'
if not project.exists():
 for folder in ['Assets','Packages','ProjectSettings']:shutil.copytree(REPO/'tests/projects/physics'/folder,project/folder)
 shutil.copy2(ROOT/'Evidence/skin-dependency/ProbeEntry.cs.txt',project/'Assets/Scripts/ProbeEntry.cs')
out=WORK/('skin-'+a.label+'.json');log=out.with_suffix('.log')
if out.exists() or log.exists():raise SystemExit('Refusing to overwrite '+str(out))
env=dict(os.environ,PHYSICS_PROBE_OUT=str(out),UNBOUND_MANAGED_DIR=str(WORK/('managed-baseline' if a.baseline else 'managed-final')))
env['LD_LIBRARY_PATH']=env['UNBOUND_MANAGED_DIR']
with log.open('w') as f:subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'),'timeout','300',os.environ['SCENE_SETTINGS_CLI'],'run','-p',str(project),'--headless','30000','--no-watch'],env=env,stdout=f,stderr=subprocess.STDOUT,check=True)
print(out,flush=True)
