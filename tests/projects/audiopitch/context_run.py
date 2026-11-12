#!/usr/bin/env python3
"""Replay an independent surround context profile against its committed PCM reference."""
import argparse
import datetime
import os
import json
import shutil
from pathlib import Path
import subprocess
import sys

fixture = Path(__file__).resolve().parent
sys.path.insert(0, str(fixture.parents[1]))
from probe_paths import shared_runtime
from common import profile, context_sessions

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--profile', choices=('transitions', 'wet', 'wet-edges', 'settings', 'prologic', 'zones', 'mono', 'wide', 'wide-pcm', 'listener-scripts'), default='transitions')
p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
p.add_argument('--rate', type=int, choices=(48000, 44100), action='append')
p.add_argument('--work', type=Path)
a = p.parse_args()
if a.profile in ('settings', 'wide', 'wide-pcm') and a.rate and a.rate != [48000]:
    p.error('settings controls use a 48000 Hz session with per-case configurations')
if not a.unbound:
    p.error('--unbound or $UNBOUND is required')
cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/validation/runs'
work = a.work or cache/('audio-'+a.profile+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
work.mkdir(parents=True, exist_ok=False)
runtime = shared_runtime(a.unbound, work/'runtime')
subprocess.run([sys.executable, str(fixture.parents[1]/'authored_project.py'), 'assemble', fixture.name,
                'default', '--release', a.release, '--out', str(work/'project')], check=True)
if a.profile in ('wide', 'wide-pcm'):
    from wide_boot import prepare
    prepare(work/'project')
failed = False
for rate in a.rate or ((48000,) if a.profile in ('settings', 'wide', 'wide-pcm') else (48000, 44100)):
    if a.profile == 'prologic':
        from prologic_pairs import compare_pairs
        reference = fixture/'Reference'/('unity-'+a.release)/'prologic'/('rate-'+str(rate))
        if compare_pairs(reference, work/('native-pairs-'+str(rate)+'.jsonl.gz'), a.release, rate):
            raise SystemExit('the recorded mode-pair relationship differs')
    part = work/('engine-'+str(rate))
    sessions = context_sessions(a.profile)
    merged = {}
    for session, selected in sessions.items():
        target = part/session if session else part
        subprocess.run([sys.executable, str(fixture/'engine.py'), '--release', a.release, '--rate', str(rate),
                        '--native-project', str(work/'project'), '--runtime', str(runtime), '--cases', ','.join(selected),
                        '--device-channels', '8', '--work', str(target),
                        *(['--buffer', '8192'] if a.profile in ('wide', 'wide-pcm') else [])], check=True)
        if session:
            for key, value in json.loads((target/'capture/capture.json').read_text()).items():
                if key in ('release', 'rate', 'reset_cases'):
                    assert merged.get(key, value) == value, key
                else:
                    assert key not in merged, key
                merged[key] = value
            (part/'capture').mkdir(exist_ok=True)
            for f in (target/'capture').glob('*.f32'):
                if a.profile in ('wide', 'wide-pcm'): os.link(f, part/'capture'/f.name)
                else: shutil.copy2(f, part/'capture'/f.name)
    if any(sessions):
        (part/'capture/capture.json').write_text(json.dumps(merged, indent=2)+'\n')
    for name, extra in [('strict', []), ('gate', ['--pcm-steps', '1'])]:
        result = subprocess.run([sys.executable, str(fixture/'compare.py'), '--release', a.release,
                                 '--rate', str(rate), '--profile', a.profile, '--candidate', str(part/'capture'),
                                 '--report', str(work/(name+'-'+str(rate)+'.jsonl.gz')), *extra])
        if name == 'gate':
            failed |= bool(result.returncode)
print('reports:', work)
raise SystemExit(failed)
