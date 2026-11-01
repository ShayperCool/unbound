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
p.add_argument('--diagnostic-input', action='store_true')
p.add_argument('--listener', action='store_true', help='Also capture the unmodified listener callback')
p.add_argument('--startup', action='store_true', help='Independent lowpass impulses at known startup frames')
p.add_argument('--api', action='store_true', help='Capture AudioRenderer lifecycle and buffer validation')
p.add_argument('--pcm', action='store_true', help='Capture positive/negative fractional and saturated device PCM')
p.add_argument('--gain', action='store_true', help='Capture mixer attenuation over the full volume range')
p.add_argument('--callbacks', action='store_true', help='Capture source and listener filters around mixer DSP')
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
    subprocess.run(command, env=dict(os.environ, FILTER_PROBE_OUT=str(output), FILTER_PROBE_RATE=str(a.rate), FILTER_PROBE_DIAGNOSTIC_INPUT="1" if a.diagnostic_input else "0", FILTER_PROBE_LISTENER="1" if a.listener else "0", FILTER_PROBE_STARTUP="1" if a.startup else "0", FILTER_PROBE_API="1" if a.api else "0", FILTER_PROBE_PCM="1" if a.pcm else "0", FILTER_PROBE_GAIN="1" if a.gain else "0", FILTER_PROBE_CALLBACKS="1" if a.callbacks else "0"), stdout=log, stderr=subprocess.STDOUT, check=True)
if not (output/'capture.json').exists():
    raise SystemExit('Editor completed without a capture')
if a.api:
    raise SystemExit(0)

values = array.array('f')
values.frombytes((output/('callbacks.f32' if a.callbacks else 'gain_p6.f32' if a.gain else 'pcm_output.f32' if a.pcm else 'step_lp.f32' if a.startup else 'tone_hp.f32' if a.diagnostic_input else 'dry.f32')).read_bytes())
if not any(abs(x) > 1e-6 for x in values):
    raise SystemExit('Rejected silent dry capture; no Native DSP evidence')

header = json.loads((output/'capture.json').read_text())
if header['rate'] != a.rate:
    raise SystemExit('Editor did not use the requested sample rate')
raw = array.array('f')
raw.frombytes((output/('callbacks.master.f32' if a.callbacks else 'gain_p6.master.f32' if a.gain else 'pcm_output.master.f32' if a.pcm else 'step_lp.master.f32' if a.startup else 'tone_hp.input.f32' if a.diagnostic_input else 'dry.master.f32')).read_bytes())
if len(raw) != 12288 * 2 or not any(abs(x) > 1e-4 for x in raw):
    raise SystemExit('Rejected incomplete or stale master sink capture')
