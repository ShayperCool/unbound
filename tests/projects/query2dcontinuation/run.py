#!/usr/bin/env python3
"""Private, write-once continuation controls. Each Native label gets a fresh project."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
p = argparse.ArgumentParser()
p.add_argument('mode', choices=['prepare', 'native', 'engine'])
p.add_argument('--label', required=True)
a = p.parse_args()
work = Path(os.environ['QUERY_WORK'])
project = work / ('initial-' + a.label)
if a.mode == 'prepare':
    if project.exists():
        raise SystemExit(f'Refusing to replace {project}')
    for name in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(REPO / 'tests/projects/physics' / name, project / name)
    shutil.copyfile(ROOT / 'Sources/InitialOverlap.cs', project / 'Assets/Scripts/ProbeEntry.cs')
else:
    out = work / (a.label + '.json')
    if out.exists():
        raise SystemExit(f'Refusing to replace {out}')
    if a.mode == 'native':
        command = [str(Path.home() / 'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work / (a.label + '-editor.log'))]
    else:
        command = [os.environ['SCENE_SETTINGS_CLI'], 'run', '-p', str(project), '--headless', '30000', '--no-watch']
    with (work / (a.label + '.log')).open('x') as log:
        subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '600', *command], env=dict(os.environ, PHYSICS_PROBE_OUT=str(out)), stdout=log, stderr=subprocess.STDOUT, check=True)
