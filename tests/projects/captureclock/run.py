#!/usr/bin/env python3
"""Capture the first three frames after an Awake capture-duration assignment."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['native', 'engine'])
p.add_argument('--release', required=True)
p.add_argument('--label', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--duration', required=True, type=float)
p.add_argument('--cli', type=Path)
p.add_argument('--runtime', type=Path)
a = p.parse_args()
if Path(a.label).name != a.label or a.label in ['', '.', '..']:
    p.error('label must be a single directory name')
try:
    cache_artifact_path(a.work)
except ValueError as error:
    p.error(str(error))
work = a.work.resolve() / a.label
work.mkdir(parents=True, exist_ok=False)
project = work / 'project'
for directory in ['Assets', 'Packages', 'ProjectSettings']:
    shutil.copytree(ROOT.parent / 'particles' / directory, project / directory)
shutil.rmtree(project / 'Assets/Scripts')
(project / 'Assets/Scripts').mkdir()
shutil.copy2(ROOT / 'Sources/CaptureClockProbe.cs', project / 'Assets/Scripts/CaptureClockProbe.cs')
(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + a.release + '\n')
output = work / 'capture.jsonl'
env = dict(os.environ, CAPTURE_CLOCK_DURATION=str(a.duration), CAPTURE_CLOCK_OUT=str(output))
if a.mode == 'native':
    editor = os.environ.get('UNBOUND_PROBE_UNITY', str(Path.home() / 'Unity/Hub/Editor' / a.release / 'Editor/Unity'))
    command = [editor, '-batchmode', '-nographics', '-projectPath', str(project),
               '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work / 'editor.log')]
else:
    if not a.cli or not a.runtime:
        p.error('engine requires a matching --cli and --runtime')
    env.update(UNBOUND_MANAGED_DIR=str(a.runtime.resolve()), LD_LIBRARY_PATH=str(a.runtime.resolve()))
    command = [str(a.cli.resolve()), 'run', '-p', str(project), '--headless', '5', '--no-watch']
command = [str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '600', *command]
audit = {'command': command, 'duration': a.duration}
if a.mode == 'engine':
    audit['sha256'] = {str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                       for path in [a.cli, a.runtime / 'UnityEngine.CoreModule.dll', a.runtime / 'libunbound_physx.so']}
(work / 'command.json').write_text(json.dumps(audit, indent=2) + '\n')
with (work / 'stdout.log').open('x') as log:
    subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
rows = [json.loads(line) for line in output.read_text().splitlines()]
if len(rows) != 3 or [row['frame'] for row in rows] != [0, 1, 2]:
    raise SystemExit('Incomplete capture: ' + str(output))
print(output, flush=True)
