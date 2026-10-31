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
p.add_argument('--native-project', type=Path, required=True, help='Private mixer assets created by native.py')
p.add_argument('--runtime', type=Path, required=True, help='Directory containing matching unbound and managed/')
p.add_argument('--work', type=Path, required=True)
p.add_argument('--rate', type=int, choices=(44100, 48000), default=48000)
p.add_argument('--renderer', action='store_true', help='Use the real production AudioRenderer API')
p.add_argument('--listener', action='store_true')
p.add_argument('--startup', action='store_true')
p.add_argument('--pcm', action='store_true')
p.add_argument('--api', action='store_true')
p.add_argument('--diagnostic-input', action='store_true')
p.add_argument('--gain', action='store_true')
p.add_argument('--callbacks', action='store_true')
a = p.parse_args()
a.work.mkdir(parents=True, exist_ok=False)
project = a.work / 'project'
for name in ('Assets', 'Packages', 'ProjectSettings'):
    shutil.copytree(a.native_project/name, project/name)
# Always use the current source-owned runtime probe, including the engine callback path.
for name in ('FilterProbe.cs', 'RendererApiProbe.cs'):
    shutil.copy2(Path(__file__).parent/'Assets/Scripts'/name, project/'Assets/Scripts'/name)
output = a.work/'capture'
output.mkdir()
command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600', str(a.runtime/'unbound'), 'run', '-p', str(project), '--scene', 'Assets/Scenes/Probe.unity', '--headless', '60', '--no-watch', '--fixed-fps', '60']
artifacts = [a.runtime/'unbound', *(a.runtime/'managed').glob('*')]
hashes = {str(f.relative_to(a.runtime)): hashlib.sha256(f.read_bytes()).hexdigest() for f in artifacts if f.is_file()}
(a.work/'runtime-hashes.json').write_text(json.dumps(hashes, indent=2)+'\n')
(a.work/'command.json').write_text(json.dumps(command, indent=2)+'\n')
env = dict(os.environ, FILTER_PROBE_OUT=str(output), FILTER_PROBE_ENGINE="1", FILTER_PROBE_RATE=str(a.rate), FILTER_PROBE_RENDERER="1" if a.renderer else "0", FILTER_PROBE_LISTENER="1" if a.listener else "0", FILTER_PROBE_STARTUP="1" if a.startup else "0", FILTER_PROBE_PCM="1" if a.pcm else "0", FILTER_PROBE_API="1" if a.api else "0", FILTER_PROBE_DIAGNOSTIC_INPUT="1" if a.diagnostic_input else "0", FILTER_PROBE_GAIN="1" if a.gain else "0", FILTER_PROBE_CALLBACKS="1" if a.callbacks else "0", UNBOUND_MANAGED_DIR=str(a.runtime/'managed'), LD_LIBRARY_PATH=str(a.runtime/'managed'))
with (a.work/'engine.log').open('w') as log:
    subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
if not (output/'capture.json').exists():
    raise SystemExit('Engine completed without a capture; see engine.log')
