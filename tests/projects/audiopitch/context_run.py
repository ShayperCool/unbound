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
import time

fixture = Path(__file__).resolve().parent
sys.path.insert(0, str(fixture.parents[1]))
from probe_paths import shared_runtime
from common import context_sessions

sys.path.insert(0, str(fixture.parents[2]/'tools/agents'))
from probe_runtime import stop_tree

# Keep four drivers ready without reserving engines outside their own launcher.
DEFAULT_CAPTURE_JOBS = 4
CAPTURE_POLL_SECONDS = 0.1


def run_captures(captures, jobs):
    """Bound independent drivers; engine.py admits each engine through the shared pool.

    A failed driver stops the remaining drivers and leaves queued captures unstarted.
    Each driver writes a private log; merging happens later in declared session order.
    """
    if jobs < 1:
        raise ValueError('jobs must be positive')
    pending = iter(captures)
    active = {}
    exhausted = False
    try:
        while active or not exhausted:
            # Observe every completed result before starting a replacement.
            for process, (command, log) in list(active.items()):
                code = process.poll()
                if code is not None:
                    del active[process]
                    print(f'capture: exit {code}; log {log}', flush=True)
                    if code:
                        raise subprocess.CalledProcessError(code, command)
            while not exhausted and len(active) < jobs:
                capture = next(pending, None)
                if capture is None:
                    exhausted = True
                    break
                command, log = capture
                log.parent.mkdir(parents=True, exist_ok=True)
                with log.open('x') as out:
                    process = subprocess.Popen(command, stdout=out, stderr=subprocess.STDOUT,
                                               start_new_session=True)
                active[process] = (command, log)
                print(f'capture: started {process.pid}; log {log}', flush=True)
            if active:
                time.sleep(CAPTURE_POLL_SECONDS)
    finally:
        for process in active:
            stop_tree(process)


def merge_sessions(part, sessions, *, hardlink):
    """Merge only completed captures, keeping the existing session order and checks."""
    merged = {}
    for session in sessions:
        if not session:
            continue
        target = part/session
        for key, value in json.loads((target/'capture/capture.json').read_text()).items():
            if key in ('release', 'rate', 'reset_cases'):
                assert merged.get(key, value) == value, key
            else:
                assert key not in merged, key
            merged[key] = value
        (part/'capture').mkdir(exist_ok=True)
        for f in (target/'capture').glob('*.f32'):
            if hardlink:
                os.link(f, part/'capture'/f.name)
            else:
                shutil.copy2(f, part/'capture'/f.name)
    if any(sessions):
        (part/'capture/capture.json').write_text(json.dumps(merged, indent=2)+'\n')


def main(argv=None):
    p = argparse.ArgumentParser()
    p.add_argument('--release', required=True)
    p.add_argument('--profile', choices=('transitions', 'wet', 'wet-edges', 'settings', 'prologic', 'zones', 'mono', 'wide', 'wide-pcm', 'listener-scripts', 'encoding', 'schedule', 'listener-chain', 'listener-diagnostics', 'mono-live', 'topology-diagnostics', 'topology-pcm', 'voice-input'), default='transitions')
    p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
    p.add_argument('--rate', type=int, choices=(48000, 44100), action='append')
    p.add_argument('--work', type=Path)
    p.add_argument('--jobs', type=int, default=DEFAULT_CAPTURE_JOBS, help='Maximum concurrent capture drivers; each engine takes its own heavy-job lease')
    p.add_argument('--device-channels', type=int, choices=(2, 4, 6, 8), default=8, help='Private encoding-reference device width')
    a = p.parse_args(argv)
    if a.device_channels != 8 and a.profile != 'encoding': p.error('--device-channels requires --profile encoding')
    if a.jobs < 1:
        p.error('--jobs must be positive')
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
        if a.profile in ('prologic', 'encoding'):
            from encoding_pairs import compare_encoding_pairs
            for width in ((2, 4, 6, 8) if a.profile == 'prologic' else (a.device_channels,)):
                base = fixture/'Reference'/('unity-'+a.release)
                availability = (base/'encoding' if width == 8 else base/'encoding-devices'/('device-'+str(width)))/('rate-'+str(rate))
                if compare_encoding_pairs(availability, work/f'native-six-input-device-{width}-pairs-{rate}.jsonl.gz', a.release, rate, width):
                    raise SystemExit('the recorded six-input availability relationship differs')
        if a.profile == 'prologic':
            from prologic_pairs import compare_pairs
            reference = fixture/'Reference'/('unity-'+a.release)/'prologic'/('rate-'+str(rate))
            if compare_pairs(reference, work/('native-pairs-'+str(rate)+'.jsonl.gz'), a.release, rate):
                raise SystemExit('the recorded mode-pair relationship differs')
        part = work/('engine-'+str(rate))
        sessions = context_sessions(a.profile)
        captures = []
        for index, (session, selected) in enumerate(sessions.items()):
            target = part/session if session else part
            command = [sys.executable, str(fixture/'engine.py'), '--release', a.release, '--rate', str(rate),
                       '--native-project', str(work/'project'), '--runtime', str(runtime), '--cases', ','.join(selected),
                       '--device-channels', str(a.device_channels), '--work', str(target),
                       *(['--buffer', '8192'] if a.profile in ('wide', 'wide-pcm') else [])]
            captures.append((command, work/'drivers'/f'{rate}-{index:03d}.log'))
        run_captures(captures, a.jobs)
        merge_sessions(part, sessions, hardlink=a.profile in ('wide', 'wide-pcm'))
        reference = fixture/'Reference'/('unity-'+a.release)
        reference = (reference/'encoding-devices'/('device-'+str(a.device_channels)) if a.profile == 'encoding' and a.device_channels != 8 else reference/a.profile)/('rate-'+str(rate))
        for name, extra in [('strict', []), ('gate', ['--pcm-steps', '1'])]:
            result = subprocess.run([sys.executable, str(fixture/'compare.py'), '--release', a.release,
                                     '--rate', str(rate), '--profile', a.profile, '--candidate', str(part/'capture'),
                                     '--report', str(work/(name+'-'+str(rate)+'.jsonl.gz')), '--reference', str(reference), *extra])
            if name == 'gate':
                failed |= bool(result.returncode)
    print('reports:', work)
    return int(failed)


if __name__ == '__main__':
    raise SystemExit(main())
