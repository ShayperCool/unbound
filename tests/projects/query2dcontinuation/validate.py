#!/usr/bin/env python3
"""Run existing gates privately, then add current lifecycle/loading controls."""
import os
from pathlib import Path
import shutil
import subprocess

REPO = Path(__file__).resolve().parents[3]
WORK = Path(os.environ['QUERY_WORK'])
os.environ['SCENE_SETTINGS_CLI']=str(Path(__file__).resolve().with_name('private_cli.py'))
def run(*args):
    subprocess.run(list(map(str,args)),check=True)
# This driver writes to QUERY_WORK, SCENE_WORK, SCENE_PARENT_WORK and TRIGGER_WORK.
run('python3',REPO/'tests/projects/query2dparity/validate.py')
for probe in ['lifecycle','receivers']:
    for mode in ['prepare','engine']:
        run('python3',REPO/'tests/projects/trigger2dlifecycle/run.py',mode,'--probe',probe,'--label','final')
loading=WORK/'loading'
(loading/'logs').mkdir(parents=True)
os.environ['LOADING_WORK']=str(loading)
# Reuse the existing Unity-authored assets. Never generate or edit repository assets.
for folder in ['Assets','Packages','ProjectSettings']:
    shutil.copytree(REPO/'tests/projects/inactive3dloading'/folder,loading/'created'/folder)
for order in ['transform-first','rigidbody-first']:
    for mode in ['prepare','engine']:
        run('python3',REPO/'tests/projects/inactive3dloading/run.py',mode,'--order',order,'--label','final')
