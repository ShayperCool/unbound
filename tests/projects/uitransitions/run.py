#!/usr/bin/env python3
"""Run in a disposable cache copy. This driver wraps its heavy children itself."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path


def run(fixture=None, method='TransitionRunner.Record', variable='UITRANSITIONS_OUT'):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=['native', 'engine'])
    p.add_argument('--release', required=True)
    p.add_argument('--output', type=Path, required=True, help='fresh directory under the Unbound cache')
    p.add_argument('--editor', type=Path)
    p.add_argument('--cli', type=Path)
    p.add_argument('--managed', type=Path)
    a = p.parse_args()
    fixture = fixture or Path(__file__).resolve().parent
    output = a.output.expanduser().resolve()
    try:
        cache_artifact_path(output)
    except ValueError as error:
        p.error(str(error))
    if output.exists():
        p.error('--output must be fresh; previous captures are never overwritten')
    output.mkdir(parents=True)
    project = output/'project'
    for name in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(fixture/name, project/name)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')
    env = os.environ.copy()
    env.pop('WT', None)
    env[variable] = str(output/'capture')
    heavy = Path.home()/'.cache/compat/with-heavy-job'
    if a.mode == 'native':
        editor = a.editor or Path.home()/f'Unity/Hub/Editor/{a.release}/Editor/Unity'
        command = [str(editor), '-batchmode', '-nographics', '-projectPath', str(project),
                   '-executeMethod', method, '-logFile', str(output/'editor.log')]
    else:
        if not a.cli or not a.managed:
            p.error('engine requires --cli and --managed')
        env['UNBOUND_MANAGED_DIR'] = str(a.managed.resolve())
        command = [str(a.cli.resolve()), 'run', '-p', str(project), '--headless', '100', '--fixed-fps', '60', '--no-watch']
    hashes = {}
    if a.mode == 'engine':
        for path in [a.cli.resolve(), a.managed.resolve()/'UnityEngine.CoreModule.dll', a.managed.resolve()/'libunbound_physx.so']:
            hashes[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    (output/'execution.json').write_text(json.dumps({'command': command, 'release': a.release, 'sha256': hashes}, indent=2)+'\n')
    with (output/'process.log').open('w') as log:
        subprocess.run([str(heavy), 'timeout', '240', *command], env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    for path, expected in hashes.items():
        if hashlib.sha256(Path(path).read_bytes()).hexdigest() != expected:
            raise RuntimeError('runtime changed during capture: ' + path)


if __name__ == '__main__':
    run()
