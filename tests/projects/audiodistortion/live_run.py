#!/usr/bin/env python3
"""Engine-only live effect gate using committed authored mixers and unchanged float bounds."""
import argparse
import datetime
import os
from pathlib import Path
import subprocess
import sys

FIXTURE = Path(__file__).resolve().parent
sys.path.insert(0, str(FIXTURE.parents[1]))
from probe_paths import shared_runtime

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--rate', type=int, choices=(48000, 44100), action='append')
p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
p.add_argument('--work', type=Path)
a = p.parse_args()
if not a.unbound:
    p.error('--unbound or $UNBOUND is required')
cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/q87-live'
work = a.work or cache/('gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
work.mkdir(parents=True, exist_ok=False)
runtime = shared_runtime(a.unbound, work/'runtime')
subprocess.run([sys.executable, str(FIXTURE.parents[1]/'authored_project.py'), 'assemble',
                FIXTURE.name, 'live', '--release', a.release, '--out', str(work/'authored')], check=True)
failed = False
for rate in a.rate or (48000, 44100):
    engine = subprocess.run([sys.executable, str(FIXTURE/'engine.py'), '--profile', 'live', '--release', a.release,
                    '--rate', str(rate), '--native-project', str(work/'authored'), '--runtime', str(runtime),
                    '--work', str(work/f'engine-{rate}')])
    failed |= engine.returncode != 0
    from parameter_trace import verify as verify_trace
    verify_trace(work/f'engine-{rate}/kernel/engine.log', a.release, rate)
    for label, steps in (('strict', 0), ('gate', 1)):
        result = subprocess.run([sys.executable, str(FIXTURE/'live.py'), '--release', a.release,
                                 '--rate', str(rate), '--candidate', str(work/f'engine-{rate}/capture'),
                                 '--report', str(work/f'{label}-{rate}.jsonl.gz'), '--pcm-steps', str(steps)])
        if label == 'gate':
            failed |= result.returncode != 0
print('live reports:', work)
raise SystemExit(failed)
