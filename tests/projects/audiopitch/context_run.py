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
from common import profile, settings_sessions

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--profile', choices=('transitions', 'wet', 'wet-edges', 'settings'), default='transitions')
p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
p.add_argument('--rate', type=int, choices=(48000, 44100), action='append')
p.add_argument('--work', type=Path)
a = p.parse_args()
if a.profile == 'settings' and a.rate and a.rate != [48000]:
    p.error('settings controls use a 48000 Hz session with per-case configurations')
if not a.unbound:
    p.error('--unbound or $UNBOUND is required')
cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/validation/runs'
work = a.work or cache/('audio-'+a.profile+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
work.mkdir(parents=True, exist_ok=False)
runtime = shared_runtime(a.unbound, work/'runtime')
subprocess.run([sys.executable, str(fixture.parents[1]/'authored_project.py'), 'assemble', fixture.name,
                'default', '--release', a.release, '--out', str(work/'project')], check=True)
failed = False
for rate in a.rate or ((48000,) if a.profile == 'settings' else (48000, 44100)):
    part = work/('engine-'+str(rate))
    sessions = settings_sessions() if a.profile == 'settings' else {'': list(profile(a.profile, rate))}
    merged = {}
    for session, selected in sessions.items():
        target = part/session if session else part
        subprocess.run([sys.executable, str(fixture/'engine.py'), '--release', a.release, '--rate', str(rate),
                        '--native-project', str(work/'project'), '--runtime', str(runtime), '--cases', ','.join(selected),
                        '--device-channels', '8', '--work', str(target)], check=True)
        if session:
            for key, value in json.loads((target/'capture/capture.json').read_text()).items():
                if key in ('release', 'rate'):
                    assert merged.get(key, value) == value, key
                else:
                    assert key not in merged, key
                merged[key] = value
            (part/'capture').mkdir(exist_ok=True)
            for f in (target/'capture').glob('*.f32'): shutil.copy2(f, part/'capture'/f.name)
    if a.profile == 'settings':
        (part/'capture/capture.json').write_text(json.dumps(merged, indent=2)+'\n')
    for name, extra in [('strict', []), ('gate', ['--pcm-steps', '1'])]:
        result = subprocess.run([sys.executable, str(fixture/'compare.py'), '--release', a.release,
                                 '--rate', str(rate), '--profile', a.profile, '--candidate', str(part/'capture'),
                                 '--report', str(work/(name+'-'+str(rate)+'.jsonl.gz')), *extra])
        if name == 'gate':
            failed |= bool(result.returncode)
print('reports:', work)
raise SystemExit(failed)
