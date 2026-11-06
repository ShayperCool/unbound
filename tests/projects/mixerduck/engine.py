#!/usr/bin/env python3
"""Capture through a private matching CLI/Core/shim and the production AudioRenderer."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--native-project', type=Path, required=True, help='Private mixer assets created by native.py')
p.add_argument('--runtime', type=Path, required=True, help='Directory containing matching unbound and managed/')
p.add_argument('--work', type=Path, required=True)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
a = p.parse_args()
fixture = Path(__file__).resolve().parent
if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
for name in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(a.native_project/name, project/name)
shutil.copy2(fixture/'Assets/Scripts/DuckProbe.cs', project/'Assets/Scripts/DuckProbe.cs')
output = a.work/'capture'; output.mkdir()
command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '900', str(a.runtime/'unbound'), 'run', '-p', str(project),
           '--scene', 'Assets/Scenes/Probe.unity', '--headless', '60', '--no-watch', '--fixed-fps', '60']
artifacts = [a.runtime/'unbound', *sorted((a.runtime/'managed').rglob('*'))]
hashes = {str(f.relative_to(a.runtime)): hashlib.sha256(f.read_bytes()).hexdigest() for f in artifacts if f.is_file()}
(a.work/'runtime-hashes.json').write_text(json.dumps(hashes, indent=2)+'\n')
(a.work/'command.json').write_text(json.dumps(command, indent=2)+'\n')
env = dict(os.environ, DUCK_PROBE_OUT=str(output), DUCK_PROBE_RATE=str(a.rate), DUCK_PROBE_ENGINE='1',
           UNBOUND_MANAGED_DIR=str(a.runtime/'managed'), LD_LIBRARY_PATH=str(a.runtime/'managed'))
with (a.work/'engine.log').open('w') as log:
    result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
(a.work/'exit.json').write_text(json.dumps({'returncode': result.returncode})+'\n')
if result.returncode: raise SystemExit(result.returncode)
from verify import verify
verify(output, a.release, a.rate)
