#!/usr/bin/env python3
"""Gate: the committed Editor-authored project, engine capture through $UNBOUND, gate-profile comparison.

Writes the strict all-case reports as well; only the gate profile (common.profile('gate'), one device
PCM step) decides the exit status. Every run directory lives under the cache.

The engine runs on the authored set Authored/unity-<release>/default (tests/authored_project.py),
so no Editor starts. --native authors a fresh private Native project per rate in the Editor
instead, as the reference capture did; when the fixture changes, record such a project again.
"""
import argparse
import datetime
import os
from pathlib import Path
import shutil
import subprocess
import sys

fixture = Path(__file__).resolve().parent
p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--rate', type=int, choices=(44100, 48000), action='append')
p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
p.add_argument('--work', type=Path)
p.add_argument('--native', action='store_true', help='Author a fresh Native project per rate in the Editor')
a = p.parse_args()
if not a.unbound: p.error('--unbound or $UNBOUND is required')
cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/validation/runs'
work = a.work or cache/('audio-reverb-gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
work.mkdir(parents=True, exist_ok=False)
runtime = work/'runtime'; runtime.mkdir()
shutil.copy2(a.unbound, runtime/'unbound')
shutil.copytree(Path(a.unbound).resolve().parents[1]/'managed', runtime/'managed')
if not a.native and subprocess.run([sys.executable, str(fixture.parents[1]/'authored_project.py'), 'assemble',
                                   fixture.name, 'default', '--release', a.release,
                                   '--out', str(work/'authored')]).returncode:
    raise SystemExit('no usable authored set: run with --native')
failed = False
for rate in a.rate or (48000, 44100):
    run = lambda script, *args: subprocess.run([sys.executable, str(fixture/script), '--release', a.release,
                                                '--rate', str(rate), *map(str, args)]).returncode
    if a.native and run('native.py', '--work', work/f'native-{rate}'): raise SystemExit(f'Native {rate} failed')
    project = work/f'native-{rate}/project' if a.native else work/'authored'
    if run('engine.py', '--native-project', project, '--runtime', runtime,
           '--work', work/f'engine-{rate}'): raise SystemExit(f'engine {rate} failed')
    candidate = work/f'engine-{rate}/capture'
    run('compare.py', '--profile', 'all', '--candidate', candidate, '--report', work/f'all-{rate}.jsonl.gz')
    failed |= bool(run('compare.py', '--profile', 'gate', '--pcm-steps', '1', '--candidate', candidate,
                       '--report', work/f'gate-{rate}.jsonl.gz'))
print('reports:', work)
raise SystemExit(failed)
