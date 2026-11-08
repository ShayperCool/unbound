#!/usr/bin/env python3
"""Engine-only gate retaining all raw suspension failures and their explicit classification."""
import argparse
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
from compare import compare
from acceptance import check

FIXTURE = Path(__file__).resolve().parent
REPO = FIXTURE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import shared_runtime


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True)
    parser.add_argument('--work', type=Path)
    parser.add_argument('--runtime', type=Path)
    args = parser.parse_args()
    cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/q43'
    work = args.work or cache/('gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
    work.mkdir(parents=True, exist_ok=False)
    if args.runtime is None:
        runtime = shared_runtime(Path(os.environ['UNBOUND']), work/'runtime')
    else:
        manifest = args.runtime/'runtime-manifest.json'
        runtime = shared_runtime(args.runtime/'unbound', work/'runtime', managed=args.runtime/'managed',
                                 manifest=json.loads(manifest.read_text()) if manifest.is_file() else None)
    def run(script, *flags):
        subprocess.run([sys.executable, str(script), '--release', args.release, *map(str, flags)], check=True)
    for item in ('default', 'submission', 'lifecycle', 'generators', 'state', 'reset-data'):
        subprocess.run([sys.executable, str(REPO/'tests/authored_project.py'), 'assemble', 'mixersuspend', item,
                        '--release', args.release, '--out', str(work/('native-'+item))], check=True)
    profiles = [('suspension', 'default', [], rate) for rate in (48000, 44100)]
    profiles += [(name, name, ['--'+name], rate) for name in ('submission', 'lifecycle') for rate in (48000, 44100)]
    profiles += [('generators', 'generators', ['--generators'], 48000), ('state', 'state', [], 48000), ('state', 'state', [], 44100)]
    profiles += [('reset-data', 'reset-data', [], rate) for rate in (48000, 44100)]
    unexpected = 0
    for name, item, flags, rate in profiles:
        capture = work/f'engine-{name}-{rate}'
        if name in ('state', 'reset-data'):
            subprocess.run([sys.executable, str(FIXTURE/('reset_data.py' if name == 'reset-data' else 'state.py')), 'engine', '--release', args.release, '--rate', str(rate),
                            '--project', str(work/('native-'+name)), '--runtime', str(runtime), '--work', str(capture)], check=True)
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
