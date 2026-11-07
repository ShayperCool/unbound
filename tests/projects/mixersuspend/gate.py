#!/usr/bin/env python3
"""Engine-only gate retaining all raw suspension failures and their explicit classification."""
import argparse
import datetime
import os
from pathlib import Path
import subprocess
import sys
from compare import compare
from acceptance import check

FIXTURE = Path(__file__).resolve().parent
REPO = FIXTURE.parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True)
    parser.add_argument('--work', type=Path)
    parser.add_argument('--runtime', type=Path)
    args = parser.parse_args()
    cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/q43'
    work = args.work or cache/('gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
    work.mkdir(parents=True, exist_ok=False)
    runtime = args.runtime
    if runtime is None:
        cli = Path(os.environ['UNBOUND']).resolve()
        managed = Path(os.environ.get('UNBOUND_MANAGED_DIR') or cli.parent.parent/'managed')
        runtime = work/'runtime'; runtime.mkdir()
        (runtime/'unbound').symlink_to(cli); (runtime/'managed').symlink_to(managed)
    def run(script, *flags):
        subprocess.run([sys.executable, str(script), '--release', args.release, *map(str, flags)], check=True)
    for item in ('default', 'submission', 'lifecycle', 'generators', 'state'):
        subprocess.run([sys.executable, str(REPO/'tests/authored_project.py'), 'assemble', 'mixersuspend', item,
                        '--release', args.release, '--out', str(work/('native-'+item))], check=True)
    profiles = [('suspension', 'default', [], rate) for rate in (48000, 44100)]
    profiles += [(name, name, ['--'+name], rate) for name in ('submission', 'lifecycle') for rate in (48000, 44100)]
    profiles += [('generators', 'generators', ['--generators'], 48000), ('state', 'state', [], 48000), ('state', 'state', [], 44100)]
    unexpected = 0
    for name, item, flags, rate in profiles:
        capture = work/f'engine-{name}-{rate}'
        if name == 'state':
            subprocess.run([sys.executable, str(FIXTURE/'state.py'), 'engine', '--release', args.release, '--rate', str(rate),
                            '--project', str(work/'native-state'), '--runtime', str(runtime), '--work', str(capture)], check=True)
        else:
            run(FIXTURE/'engine.py', '--rate', rate, *flags, '--native-project', work/('native-'+item), '--runtime', runtime, '--work', capture)
        prefix = '' if name == 'suspension' else name+'-'
        reference = FIXTURE/'Reference'/('unity-'+args.release)/(prefix+f'rate-{rate}')
        report = work/f'raw-{name}-{rate}.jsonl.gz'
        compare(reference, capture/'capture', report, batch=True)
        unexpected += check(report, name, rate)
    print(f'AudioMixer suspension gate: {unexpected} unexplained failures; complete reports: {work}', flush=True)
    return bool(unexpected)


if __name__ == '__main__': raise SystemExit(main())
