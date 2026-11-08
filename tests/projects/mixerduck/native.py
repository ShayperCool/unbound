#!/usr/bin/env python3
"""Create an independent private project and capture offline Unity Duck Volume, Send and Receive audio."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
p.add_argument('--cases', help='Comma-separated subset, for diagnostics only')
a = p.parse_args()
fixture = Path(__file__).resolve().parent
if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
shutil.copytree(fixture, project, ignore=shutil.ignore_patterns('Reference', 'Library', 'Logs', '__pycache__', '*.py', '*.md'))
output = a.work/'capture'
editor = a.editor or Path.home()/'Unity/Hub/Editor'/a.release/'Editor/Unity'
command = [str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'editor-gpu', 'timeout', '900', str(editor), '-batchmode', '-nographics',
           '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(a.work/'editor.log')]
(a.work/'command.json').write_text(json.dumps(command, indent=2)+'\n')
env = dict(os.environ, DUCK_PROBE_OUT=str(output), DUCK_PROBE_RATE=str(a.rate))
if a.cases: env['DUCK_PROBE_CASES'] = a.cases
with (a.work/'stdout.log').open('w') as log:
    subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
if not a.cases:
    import sys
    sys.path.insert(0, str(fixture))
    from verify import verify
    verify(output, a.release, a.rate)
