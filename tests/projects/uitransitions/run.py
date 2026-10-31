#!/usr/bin/env python3
"""Run in a disposable cache copy. This driver wraps its heavy children itself."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess


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
    cache = Path(os.environ.get('XDG_CACHE_HOME', str(Path.home()/'.cache')))/'unbound'
    output = Path(os.path.abspath(a.output.expanduser()))
    if not output.is_relative_to(Path(os.path.abspath(cache))):
        p.error('--output must be under $XDG_CACHE_HOME/unbound')
    if output.exists():
        p.error('--output must be fresh; previous captures are never overwritten')
    if a.release != (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(': ')[1]:
        p.error('fixture release does not match --release')
    output.mkdir(parents=True)
    project = output/'project'
    for name in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(fixture/name, project/name)
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
    with (output/'process.log').open('w') as log:
        subprocess.run([str(heavy), 'timeout', '240', *command], env=env, stdout=log, stderr=subprocess.STDOUT, check=True)


if __name__ == '__main__':
    run()
