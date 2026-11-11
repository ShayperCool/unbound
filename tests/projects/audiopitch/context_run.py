#!/usr/bin/env python3
"""Replay an independent surround context profile against its committed PCM reference."""
import argparse
import datetime
import os
from pathlib import Path
import subprocess
import sys

fixture = Path(__file__).resolve().parent
sys.path.insert(0, str(fixture.parents[1]))
from probe_paths import shared_runtime
from common import transition_cases

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
p.add_argument('--rate', type=int, choices=(48000, 44100), action='append')
p.add_argument('--work', type=Path)
a = p.parse_args()
if not a.unbound:
    p.error('--unbound or $UNBOUND is required')
cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/validation/runs'
work = a.work or cache/('audio-transitions-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
work.mkdir(parents=True, exist_ok=False)
runtime = shared_runtime(a.unbound, work/'runtime')
subprocess.run([sys.executable, str(fixture.parents[1]/'authored_project.py'), 'assemble', fixture.name,
                'default', '--release', a.release, '--out', str(work/'project')], check=True)
failed = False
for rate in a.rate or (48000, 44100):
    part = work/('engine-'+str(rate))
    subprocess.run([sys.executable, str(fixture/'engine.py'), '--release', a.release, '--rate', str(rate),
                    '--native-project', str(work/'project'), '--runtime', str(runtime), '--cases', ','.join(transition_cases()),
                    '--device-channels', '8', '--work', str(part)], check=True)
    for name, extra in [('strict', []), ('gate', ['--pcm-steps', '1'])]:
        result = subprocess.run([sys.executable, str(fixture/'compare.py'), '--release', a.release,
                                 '--rate', str(rate), '--profile', 'transitions', '--candidate', str(part/'capture'),
                                 '--report', str(work/(name+'-'+str(rate)+'.jsonl.gz')), *extra])
        if name == 'gate':
            failed |= bool(result.returncode)
print('reports:', work)
raise SystemExit(failed)
