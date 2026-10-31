#!/usr/bin/env python3
"""Replay frozen/starting/final assemblies into private, write-once query captures."""
import argparse
import os
from pathlib import Path
import subprocess
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from probe_paths import run_root

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
p = argparse.ArgumentParser()
p.add_argument('--label', required=True)
a = p.parse_args()
work = Path(os.environ.get('QUERY_WORK',str(run_root(ROOT))))
work.mkdir(parents=True,exist_ok=True)
os.environ['QUERY_WORK']=str(work)
for probe in ['queries', 'geometry', 'selection', 'selection-fine', 'boundary', 'tests']:
    runner = REPO / 'tests/projects/query2dparity/run.py'
    subprocess.run(['python3', str(runner), 'prepare', '--probe', probe, '--label', probe+'-'+a.label], check=True)
    subprocess.run(['python3', str(runner), 'engine', '--probe', probe, '--label', probe + '-' + a.label], check=True)
env = dict(os.environ, SKIN_WORK=str(work / 'skin'))
Path(env['SKIN_WORK']).mkdir(exist_ok=True)
runner = REPO / 'tests/projects/colliderskin2d/run.py'
for mode in ['prepare', 'engine']:
    subprocess.run(['python3', str(runner), mode, '--label', a.label], env=env, check=True)
runner = ROOT / 'run.py'
for mode in ['prepare', 'engine']:
    subprocess.run(['python3', str(runner), mode, '--label', 'initial-' + a.label], check=True)
runner = ROOT.parent/'query2dties/run.py'
env = dict(os.environ,QUERY_TIES_WORK=str(work/'ties'))
for mode in ['prepare','engine']:
    subprocess.run(['python3',str(runner),mode,'--label',a.label],env=env,check=True)
