#!/usr/bin/env python3
"""Native and engine runs with version-selected Editor and external output paths."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, reference_root, run_root, unity_version, editor_path
CLI = os.environ.get('UNBOUND_CLI', os.environ.get('SCENE_SETTINGS_CLI', shutil.which('unbound') or str(REPO/'target/release/unbound')))
HEAVY = Path.home() / '.cache/compat/with-heavy-job'


def main():
    p = argparse.ArgumentParser()
    p.add_argument('mode', choices=['prepare', 'native', 'engine', 'regressions'])
    p.add_argument('--probe', choices=['timing', 'order'], default='timing')
    p.add_argument('--label', default='baseline')
    a = p.parse_args()
    work = Path(os.environ.get('TRIGGER_WORK', os.environ.get('LIFECYCLE_WORK', run_root(ROOT)))).resolve()
    work.mkdir(parents=True, exist_ok=True)
    project = work / ('project' if a.probe == 'timing' else 'project-order')
    if a.mode == 'prepare':
        for folder in ['Assets', 'Packages', 'ProjectSettings']:
            shutil.copytree(REPO/'tests/projects/physics'/folder, project/folder, dirs_exist_ok=True)
        shutil.copyfile(ROOT/'Sources'/('ProbeEntry.cs' if a.probe == 'timing' else 'OrderEntry.cs'), project/'Assets/Scripts/ProbeEntry.cs')
        # Preserve the broad fixture's input scripts and unchanged settings in the private project.
        return
    env = dict(os.environ)
    env['UNBOUND_MANAGED_DIR'] = os.environ.get('UNBOUND_MANAGED_DIR', str(work / ('managed-baseline' if a.label == 'baseline' else 'managed-final')))
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
        fixture = project if name in ['timing', 'order'] else REPO/'tests/projects'/name
        if a.mode == 'native':
            cmd = [os.environ.get('UNITY_EDITOR', str(editor_path())), '-batchmode', '-nographics',
                   '-projectPath', str(fixture), '-executeMethod', 'ProbeRunner.Record',
                   '-logFile', str(work/(stem+'.log'))]
        else:
            cmd = [CLI, 'run', '-p', str(fixture), '--headless', str(frames), '--no-watch']
        with (work/(stem+'-stdout.log')).open('w') as log:
            r = subprocess.run([str(HEAVY), 'timeout', '300', *cmd], env=env, stdout=log, stderr=subprocess.STDOUT)
        print(stem, 'exit', r.returncode, flush=True)
        if r.returncode or not out.exists():
            raise SystemExit(f'{stem} failed; see {work}')
        if a.mode == 'regressions':
            ref = reference_file(fixture) if ext else reference_root(fixture)
            with (work/(stem+'-comparison.txt')).open('w') as log:
                c = subprocess.run(['python3', str(fixture/'compare.py'), str(ref), str(out)], stdout=log, stderr=subprocess.STDOUT)
            print('comparison', c.returncode, flush=True)


if __name__ == '__main__':
    main()
