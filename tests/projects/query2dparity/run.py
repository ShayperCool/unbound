#!/usr/bin/env python3
"""Record query controls privately; each capture label is write-once."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
p = argparse.ArgumentParser()
p.add_argument('mode', choices=['prepare', 'native', 'engine', 'record-reference'])
p.add_argument('--label', default='baseline')
p.add_argument('--probe', choices=['queries','geometry','selection','selection-fine','boundary','tests'], default='queries')
p.add_argument('--repeat-label')
a = p.parse_args()
work = Path(os.environ.get('QUERY_WORK',str(run_root(ROOT))))
work.mkdir(parents=True,exist_ok=True)
project = work / ('project-'+a.probe+'-'+a.label)
if a.mode == 'record-reference':
    if not a.repeat_label:
        p.error('record-reference requires --repeat-label')
    first, second = work/(a.label+'.json'),work/(a.repeat_label+'.json')
    if first.read_bytes()!=second.read_bytes():
        raise SystemExit('Independent Native recordings differ')
    target=reference_root(ROOT)/('isolated.json' if a.probe=='tests' else a.probe+'.json')
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(first,target)
elif a.mode == 'prepare':
    for name in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(REPO / 'tests/projects/physics' / name, project / name, dirs_exist_ok=True)
    shutil.copyfile(ROOT / ('Sources/' + {'queries':'ProbeEntry.cs','geometry':'GeometryProbe.cs','selection':'ContactSelection.cs','selection-fine':'ContactSelectionFine.cs','boundary':'OwnerBoundary.cs','tests':'IsolatedTests.cs'}[a.probe]), project / 'Assets/Scripts/ProbeEntry.cs')
else:
    out = work / (a.label + '.json')
    if out.exists():
        raise SystemExit(f'Refusing to replace capture {out}')
    env = dict(os.environ, PHYSICS_PROBE_OUT=str(out))
    if a.mode == 'native':
        command = [str(editor_path()), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work / (a.label + '-editor.log'))]
    else:
        command = [os.environ['SCENE_SETTINGS_CLI'], 'run', '-p', str(project), '--headless', '30000', '--no-watch']
    with (work / (a.label + '.log')).open('x') as log:
        subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '600', *command], env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
