#!/usr/bin/env python3
"""Gate: the committed Editor-authored project, engine capture through $UNBOUND, all-case comparison.

The strict report compares every case without slack; the gate report admits one 1/32768 step of
device PCM (never of master floats) and any explicitly recorded residual (common.RESIDUALS), and
decides the exit status. Every run directory lives under the cache.

The engine runs on the authored set Authored/unity-<release>/default (tests/authored_project.py),
so no Editor starts. --native authors a fresh private Native project per rate in the Editor
instead, as the reference capture did; when the fixture changes, record such a project again.
"""
import argparse
import datetime
import os
import json
from pathlib import Path
import shutil
import subprocess
import sys

fixture = Path(__file__).resolve().parent
sys.path.insert(0, str(fixture))
from common import cases, devices, surround_cases
sys.path.insert(0, str(fixture.parents[1]))
from probe_paths import shared_runtime
p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--rate', type=int, choices=(44100, 48000), action='append')
p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
p.add_argument('--work', type=Path)
p.add_argument('--native', action='store_true', help='Author a fresh Native project per rate in the Editor')
a = p.parse_args()
if not a.unbound: p.error('--unbound or $UNBOUND is required')
cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/validation/runs'
work = a.work or cache/('audio-pitch-gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
work.mkdir(parents=True, exist_ok=False)
runtime = shared_runtime(a.unbound, work/'runtime')
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
    # Other previously loaded effect slots can have narrower output coverage. Capture each
    # buffer control in a fresh engine session, using the same authored inputs and runtime.
    primary = [key for key in cases() if not key.startswith('buffer_')]
    if run('engine.py', '--native-project', project, '--runtime', runtime, '--cases', ','.join(primary),
           '--work', work/f'engine-{rate}'): raise SystemExit(f'engine {rate} failed')
    candidate = work/f'engine-{rate}/capture'
    merged = json.loads((candidate/'capture.json').read_text())
    for key in (key for key in cases() if key.startswith('buffer_')):
        part = work/f'engine-{rate}-{key}'
        if run('engine.py', '--native-project', project, '--runtime', runtime, '--cases', key,
               '--work', part): raise SystemExit(f'engine {rate} {key} failed')
        data = json.loads((part/'capture/capture.json').read_text())
        assert all(merged[k] == data[k] for k in ('release', 'rate'))
        for name, value in data.items():
            if name in ('release', 'rate'): continue
            assert name not in merged, name
            merged[name] = value
            if isinstance(value, dict) and 'file' in value:
                shutil.copy2(part/'capture'/value['file'], candidate/value['file'])
    (candidate/'capture.json').write_text(json.dumps(merged, indent=2)+'\n')
    # Output layouts run in their own session, as the Editor recorded them.
    layouts = work/f'engine-{rate}-layouts'
    if run('engine.py', '--native-project', project, '--runtime', runtime, '--cases', ','.join(cases(True)),
           '--device-channels', 8, '--work', layouts): raise SystemExit(f'engine {rate} layouts failed')
    compared = [('all', candidate, ''), ('layouts', layouts/'capture', '-layouts')]
    surround = work/f'engine-{rate}-surround'
    if run('engine.py', '--native-project', project, '--runtime', runtime, '--cases', ','.join(surround_cases()),
           '--device-channels', 8, '--work', surround): raise SystemExit(f'engine {rate} surround failed')
    compared.append(('surround', surround/'capture', '-surround'))
    if rate == 48000:
        # Each device control on a virtual device of its channel count, as the Editor recorded.
        merged = {}
        for key, channels in devices():
            part = work/f'engine-{rate}-{key}'
            if run('engine.py', '--native-project', project, '--runtime', runtime, '--cases', key,
                   '--device-channels', channels, '--work', part): raise SystemExit(f'engine {key} failed')
            merged.update(json.loads((part/'capture/capture.json').read_text()))
        (work/f'engine-{rate}-devices').mkdir()
        (work/f'engine-{rate}-devices/capture.json').write_text(json.dumps(merged, indent=2)+'\n')
        compared.append(('devices', work/f'engine-{rate}-devices', '-devices'))
    for profile, root, suffix in compared:
        run('compare.py', '--profile', profile, '--candidate', root, '--report', work/f'strict-{rate}{suffix}.jsonl.gz')
        failed |= bool(run('compare.py', '--profile', profile, '--pcm-steps', '1', '--residuals', '--candidate', root,
                           '--report', work/f'gate-{rate}{suffix}.jsonl.gz'))
print('reports:', work)
raise SystemExit(failed)
