#!/usr/bin/env python3
"""Replay UI regressions using explicit private artifacts and cache-only projects."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path, reference_root, unity_version

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['before', 'after'])
p.add_argument('--cli', type=Path, required=True)
p.add_argument('--managed', type=Path, required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--baseline', type=Path, help='The previous before-run directory')
p.add_argument('--release', default=unity_version())
a = p.parse_args()
if a.mode == 'after' and a.baseline is None:
    p.error('after requires --baseline')
try:
    cache_artifact_path(a.work)
except ValueError as error:
    p.error(str(error))
work = a.work.resolve()
if work.exists():
    p.error('--work must be fresh')
for path in [a.cli, a.managed/'UnityEngine.CoreModule.dll', a.managed/'libunbound_physx.so']:
    if not path.is_file():
        p.error('missing runtime artifact: '+str(path))
if a.baseline and not (a.baseline/'uitk/record.jsonl').is_file():
    p.error('--baseline must contain uitk/record.jsonl')
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
work.mkdir(parents=True)
env = dict(os.environ, UNBOUND_MANAGED_DIR=str(a.managed.resolve()), LD_LIBRARY_PATH=str(a.managed.resolve()))
env.pop('WT', None)
heavy = Path.home()/'.cache/compat/with-heavy-job'
hashes = {str(path.resolve()):hashlib.sha256(path.read_bytes()).hexdigest()
          for path in [a.cli, a.managed/'UnityEngine.CoreModule.dll', a.managed/'libunbound_physx.so']}


def run(name, command, extra=None):
    with (work/(name+'.log')).open('x') as log:
        subprocess.run(list(map(str, command)), cwd=REPO, env=dict(env, **(extra or {})),
                       stdout=log, stderr=subprocess.STDOUT, check=True)


def capture(name, probe, variable, frames, size, color=None):
    project = work/'projects'/name
    for folder in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(REPO/'tests/projects'/probe/folder, project/folder)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')
    if color is not None:
        settings = project/'ProjectSettings/ProjectSettings.asset'
        text, count = re.subn(r'm_ActiveColorSpace: [01]', 'm_ActiveColorSpace: '+str(color), settings.read_text())
        if count != 1:
            raise ValueError('expected one color-space setting')
        settings.write_text(text)
    out = work/name
    out.mkdir()
    value = out/'record.jsonl' if probe == 'uitk' else out
    run(name, [heavy, 'timeout', '300', a.cli.resolve(), 'run', '-p', project,
               '--frames', frames, '--size', size, '--no-watch'], {variable:str(value)})
    return out


def reference(probe):
    versioned = reference_root(REPO/'tests/projects'/probe)
    if versioned.is_dir():
        return versioned
    fixture = REPO/'tests/projects'/probe
    version = (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(': ')[1]
    if version != a.release:
        raise ValueError('no reference for '+probe+' and Unity '+a.release)
    return fixture/'Reference'


capture('uitk', 'uitk', 'UITK_PROBE_OUT', '35', '960x540')
if a.mode == 'after':
    for name, color, profile in [('colors-linear', 1, 'Linear'), ('colors-gamma', 0, 'Gamma')]:
        out = capture(name, 'uicolors', 'UI_COLORS_OUT', '8', '128x128', color)
        run(name+'-compare', [sys.executable, REPO/'tests/projects/uicolors/compare.py', reference('uicolors')/profile, out, '-q'])
    out = capture('lifecycle', 'canvaslifecycle', 'CANVAS_LIFECYCLE_OUT', '24', '640x480')
    run('lifecycle-compare', [sys.executable, REPO/'tests/projects/canvaslifecycle/compare.py', reference('canvaslifecycle'), out])
    run('transitions', [sys.executable, REPO/'tests/projects/uitransitions/run.py', 'engine',
                       '--release', a.release, '--output', work/'transitions', '--cli', a.cli, '--managed', a.managed])
    run('transitions-compare', [sys.executable, REPO/'tests/projects/uitransitions/compare.py',
                               REPO/'tests/projects/uitransitions/Reference'/('unity-'+a.release+'.jsonl'),
                               work/'transitions/capture/transitions.jsonl'])
    run('uitk-compare', [sys.executable, REPO/'tests/projects/uitk/compare.py',
                        a.baseline.resolve()/'uitk/record.jsonl', work/'uitk/record.jsonl'])
for path, digest in hashes.items():
    if hashlib.sha256(Path(path).read_bytes()).hexdigest() != digest:
        raise RuntimeError('runtime changed during capture: '+path)
(work/'runtime-sha256.json').write_text(json.dumps(hashes, indent=2)+'\n')
print('UI regression captures complete:', work)
