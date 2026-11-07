#!/usr/bin/env python3
"""Run unchanged prior audio DSP comparators with one private matching runtime snapshot.

This engine-only ABI regression gate preserves all strict reports and original gate slack.
Sharing just the runtime snapshot avoids four redundant half-gigabyte binary copies.
"""
import argparse
import datetime
import os
from pathlib import Path
import shutil
import subprocess
import sys

fixture = Path(__file__).resolve().parent
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
p.add_argument('--work', type=Path)
a = p.parse_args()
if not a.unbound: p.error('--unbound or $UNBOUND is required')
cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/validation/runs'
work = a.work or cache/('audio-pitch-regressions-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
work.mkdir(parents=True, exist_ok=False)
runtime = work/'runtime'; runtime.mkdir()
shutil.copy2(a.unbound, runtime/'unbound')
shutil.copytree(a.unbound.resolve().parents[1]/'managed', runtime/'managed')
env = dict(os.environ, UNBOUND_SUITE_NO_EDITOR='1', UNBOUND_MANAGED_DIR=str(runtime/'managed'),
           LD_LIBRARY_PATH=str(runtime/'managed'))
failed = False
for name in ('audiodistortion', 'audiochorus', 'audioflange', 'audionormalize'):
    probe = fixture.parent/name
    run = work/name; run.mkdir()
    subprocess.run([sys.executable, str(fixture.parents[1]/'authored_project.py'), 'assemble', name,
                    'default', '--release', a.release, '--out', str(run/'authored')], env=env, check=True)
    for rate in (48000, 44100):
        def command(script, *args):
            return subprocess.run([sys.executable, str(probe/script), '--release', a.release,
                                   '--rate', str(rate), *map(str, args)], env=env).returncode
        if command('engine.py', '--native-project', run/'authored', '--runtime', runtime,
                   '--work', run/f'engine-{rate}'):
            raise SystemExit(f'{name} engine {rate} failed; reports: {work}')
        candidate = run/f'engine-{rate}/capture'
        command('compare.py', '--profile', 'all', '--candidate', candidate,
                '--report', run/f'strict-{rate}.jsonl.gz')
        failed |= bool(command('compare.py', '--profile', 'all', '--candidate', candidate,
                               '--pcm-steps', '1', '--residuals', '--report', run/f'gate-{rate}.jsonl.gz'))
# ClearFloat's comparator checks every metadata key exactly, independently of PCM slack.
failed |= bool(subprocess.run([sys.executable, str(fixture.parent/'mixerparams/run.py'), 'gate',
                              '--release', a.release, '--unbound', str(runtime/'unbound'),
                              '--work', str(work/'mixerparams')], env=env).returncode)
print('regression reports:', work, flush=True)
raise SystemExit(failed)
