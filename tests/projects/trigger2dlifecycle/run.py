#!/usr/bin/env python3
"""Private Native/engine runs, with immutable native8 runtime and explicit output paths."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
BASE = Path.home() / '.cache/compat/parity-baseline-inactive3d-native8'
HEAVY = Path.home() / '.cache/compat/with-heavy-job'


def main():
    p = argparse.ArgumentParser()
    p.add_argument('mode', choices=['prepare', 'native', 'engine', 'regressions'])
    p.add_argument('--probe', choices=['lifecycle', 'timing', 'order'], default='lifecycle')
    p.add_argument('--label', default='baseline')
    a = p.parse_args()
    work = Path(os.environ['TRIGGER_WORK']).resolve()
    project = work / ('project-' + a.probe)
    if a.mode == 'prepare':
        for folder in ['Assets', 'Packages', 'ProjectSettings']:
            shutil.copytree(REPO/'tests/projects/physics'/folder, project/folder, dirs_exist_ok=True)
        shutil.copyfile((ROOT if a.probe == 'lifecycle' else ROOT.parent/'trigger2dtiming')/'Sources'/('OrderEntry.cs' if a.probe == 'order' else 'ProbeEntry.cs'), project/'Assets/Scripts/ProbeEntry.cs')
        # Preserve the broad fixture's input scripts and unchanged settings in the private project.
        return
    env = dict(os.environ)
    env['UNBOUND_MANAGED_DIR'] = str(work / ('managed-baseline' if a.label == 'baseline' else 'managed-final'))
    env['LD_LIBRARY_PATH'] = env['UNBOUND_MANAGED_DIR']
    if a.mode == 'regressions':
        cases = [('physics', 'PHYSICS_PROBE_OUT', 600, 'json'),
                 ('joints2d', 'JOINTS2D_PROBE_OUT', 30000, ''),
                 ('effectors', 'EFFECTORS_PROBE_OUT', 30000, ''),
                 ('forces', 'FORCES_PROBE_OUT', 30000, '')]
    else:
        cases = [(a.probe, 'PHYSICS_PROBE_OUT', 30000, 'json')]
    for name, var, frames, ext in cases:
        stem = f'{name}-{a.label}'
        out = work/(stem + ('.'+ext if ext else ''))
        if not ext:
            out.mkdir(exist_ok=True)
        env[var] = str(out)
        fixture = project if name in ['lifecycle', 'timing', 'order'] else REPO/'tests/projects'/name
        if out.exists() and ext:
            raise SystemExit(f'Refusing to overwrite {out}')
        if a.mode == 'native':
            cmd = [str(Path.home()/'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'), '-batchmode', '-nographics',
                   '-projectPath', str(fixture), '-executeMethod', 'ProbeRunner.Record',
                   '-logFile', str(work/(stem+'.log'))]
        else:
            cmd = [str(BASE/'unbound'), 'run', '-p', str(fixture), '--headless', str(frames), '--no-watch']
        with (work/(stem+'-stdout.log')).open('w') as log:
            r = subprocess.run([str(HEAVY), 'timeout', '300', *cmd], env=env, stdout=log, stderr=subprocess.STDOUT)
        print(stem, 'exit', r.returncode, flush=True)
        if r.returncode or not out.exists():
            raise SystemExit(f'{stem} failed; see {work}')
        if a.mode == 'regressions':
            ref = fixture/'Reference'/('unity-6000.0.71f1.json' if ext else '')
            with (work/(stem+'-comparison.txt')).open('w') as log:
                c = subprocess.run(['python3', str(fixture/'compare.py'), str(ref), str(out)], stdout=log, stderr=subprocess.STDOUT)
            print('comparison', c.returncode, flush=True)


if __name__ == '__main__':
    main()
