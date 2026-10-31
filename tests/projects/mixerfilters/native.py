#!/usr/bin/env python3
"""Create an independent private project and capture offline Unity audio."""
import argparse
import array
import json
import os
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
p.add_argument('--rate', type=int, choices=(44100, 48000), default=48000)
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
    subprocess.run(command, env=dict(os.environ, FILTER_PROBE_OUT=str(output), FILTER_PROBE_RATE=str(a.rate)), stdout=log, stderr=subprocess.STDOUT, check=True)
if not (output/'capture.json').exists():
    raise SystemExit('Editor completed without a capture')

values = array.array('f')
values.frombytes((output/'dry.f32').read_bytes())
if not any(abs(x) > 1e-6 for x in values):
    raise SystemExit('Rejected silent dry capture; no Native DSP evidence')

header = json.loads((output/'capture.json').read_text())
if header['rate'] != a.rate:
    raise SystemExit('Editor did not use the requested sample rate')
raw = array.array('f')
raw.frombytes((output/'dry.master.f32').read_bytes())
if len(raw) != 12288 * 2 or not (raw[2048 * 2] > 0.1):
    raise SystemExit('Rejected incomplete or stale master sink capture')
