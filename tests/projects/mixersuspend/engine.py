#!/usr/bin/env python3
"""Capture through a private matching CLI/Core/shim and the real listener callback."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--reset', action='store_true')
p.add_argument('--reset-playback', action='store_true')
p.add_argument('--submission', action='store_true')
p.add_argument('--lifecycle', action='store_true')
p.add_argument('--generators', action='store_true')
p.add_argument('--native-project', type=Path, required=True, help='Private mixer assets created by native.py')
p.add_argument('--runtime', type=Path, required=True, help='Directory containing matching unbound and managed/')
p.add_argument('--work', type=Path, required=True)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
a = p.parse_args()
if sum((a.reset,a.reset_playback,a.submission,a.lifecycle,a.generators))>1: p.error('choose one reset profile')
fixture = Path(__file__).resolve().parent
if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
for name in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(a.native_project/name, project/name)

# Use the exact authored sources of the selected Native experiment.
inputs={str(f.relative_to(project)):hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted((project/'Assets').rglob('*')) if f.is_file()}
(a.work/'input-hashes.json').write_text(json.dumps(inputs,indent=2)+'\n')
output = a.work/'capture'; output.mkdir()
command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '900', str(a.runtime/'unbound'), 'run', '-p', str(project), '--scene', 'Assets/Scenes/Lifecycle.unity' if a.lifecycle else 'Assets/Scenes/Submission.unity' if a.submission else 'Assets/Scenes/ResetPlayback.unity' if a.reset_playback else 'Assets/Scenes/Reset.unity' if a.reset else 'Assets/Scenes/Probe.unity', '--headless', '60' if a.reset or a.reset_playback else '3000' if a.submission else '10000' if a.lifecycle else '100000', '--no-watch', '--fixed-fps', '60']
artifacts = [a.runtime/'unbound', *sorted((a.runtime/'managed').rglob('*'))]
hashes = {str(f.relative_to(a.runtime)): hashlib.sha256(f.read_bytes()).hexdigest() for f in artifacts if f.is_file()}
(a.work/'runtime-hashes.json').write_text(json.dumps(hashes, indent=2)+'\n')
(a.work/'command.json').write_text(json.dumps(command, indent=2)+'\n')
env = dict(os.environ, UseSharedCompilation="false", MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0", SUSPEND_PROBE_OUT=str(output), SUSPEND_PROBE_RATE=str(a.rate), SUSPEND_GENERATOR_ENGINE='1' if a.generators else '',SUSPEND_PROBE_ENGINE='' if a.reset or a.reset_playback or a.submission or a.lifecycle or a.generators else '1', SUSPEND_RESET_ENGINE='1' if a.reset else '', SUSPEND_PLAYBACK_ENGINE='1' if a.reset_playback else '', SUSPEND_SUBMISSION_ENGINE='1' if a.submission else '', SUSPEND_LIFECYCLE_ENGINE='1' if a.lifecycle else '', UNBOUND_CACHE=str(a.work/'cache'), UNBOUND_MANAGED_DIR=str(a.runtime/'managed'), LD_LIBRARY_PATH=str(a.runtime/'managed'))
with (a.work/'engine.log').open('w') as log:
    result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
(a.work/'exit.json').write_text(json.dumps({'returncode':result.returncode})+'\n')
if result.returncode: raise SystemExit(result.returncode)
if a.generators:
    from verify_generators import verify
elif a.lifecycle:
    from verify_lifecycle import verify
elif a.submission:
    from verify_submission import verify
elif a.reset_playback:
    from verify_reset_playback import verify
elif a.reset:
    from verify_reset import verify
else:
    from verify import verify
verify(output, a.release, a.rate)

from pack import pack
pack(output)
