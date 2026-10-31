#!/usr/bin/env python3
"""Create an independent private project and capture offline Unity audio."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
a = p.parse_args()
fixture = Path(__file__).resolve().parent
if (fixture / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work / 'project'
shutil.copytree(fixture, project, ignore=shutil.ignore_patterns('Reference', 'Library', 'Logs', '__pycache__'))
output = a.work / 'capture'
editor = a.editor or Path.home() / 'Unity/Hub/Editor' / a.release / 'Editor/Unity'
command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600', str(editor), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(a.work/'editor.log')]
(a.work/'command.txt').write_text('\n'.join(command)+'\n')
with (a.work/'stdout.log').open('w') as log:
    subprocess.run(command, env=dict(os.environ, FILTER_PROBE_OUT=str(output)), stdout=log, stderr=subprocess.STDOUT, check=True)
if not (output/'capture.json').exists():
    raise SystemExit('Editor completed without a capture')
