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
p.add_argument('--cases', help='Selected controls for independent output-context sessions')
p.add_argument('--reset-cases', action='store_true', help='Reset output before each case, matching its Native capture')
p.add_argument('--buffer', type=int, default=1024, help='Initial session DSP buffer before per-case configuration')
p.add_argument('--device-channels', type=int, default=2,
               help='Output channels of the engine virtual device, as the Editor capture device had')
a = p.parse_args()
fixture = Path(__file__).resolve().parent
if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
for name in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(a.native_project/name, project/name)
for script in (fixture/'Assets/Scripts').glob('*.cs'): shutil.copy2(script, project/'Assets/Scripts'/script.name)
output = a.work/'capture'; output.mkdir()
command = [str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-cpu', 'timeout', '900', str(a.runtime/'unbound'), 'run', '-p', str(project),
           '--scene', 'Assets/Scenes/Probe.unity', '--headless', '60', '--no-watch', '--fixed-fps', '60']
artifacts = [a.runtime/'unbound', *sorted((a.runtime/'managed').rglob('*'))]
hashes = {str(f.relative_to(a.runtime)): hashlib.sha256(f.read_bytes()).hexdigest() for f in artifacts if f.is_file()}
(a.work/'runtime-hashes.json').write_text(json.dumps(hashes, indent=2)+'\n')
(a.work/'command.json').write_text(json.dumps(command, indent=2)+'\n')
env = dict(os.environ, EFFECT_PROBE_OUT=str(output), EFFECT_PROBE_RATE=str(a.rate), EFFECT_PROBE_ENGINE='1',
           EFFECT_PROBE_RESET_CASES='1' if a.reset_cases else '0',
           EFFECT_PROBE_BUFFER=str(a.buffer),
           UNBOUND_MANAGED_DIR=str(a.runtime/'managed'), LD_LIBRARY_PATH=str(a.runtime/'managed'))
if a.cases: env['EFFECT_PROBE_CASES'] = a.cases
env['UNBOUND_AUDIO_DEVICE_CHANNELS'] = str(a.device_channels)
with (a.work/'engine.log').open('w') as log:
    result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
(a.work/'exit.json').write_text(json.dumps({'returncode': result.returncode})+'\n')
if result.returncode: raise SystemExit(result.returncode)
from verify import verify
verify(output, a.release, a.rate, selected=a.cases.split(',') if a.cases else None)
