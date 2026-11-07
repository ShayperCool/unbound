#!/usr/bin/env python3
"""Engine-only gate for the original Birth counts and full particle histories.

The broader comparator still reports every unrelated module residual. This gate
uses that comparator unchanged and requires every original Birth record to pass.
"""
import argparse
import os
from pathlib import Path
import subprocess
import sys
import time

import compare

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import run_root  # noqa: E402


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-version', required=True)
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    reference = ROOT/'Reference'/('unity-' + args.unity_version + '.jsonl')
    candidate = args.candidate
    if candidate is None:
        out = run_root(ROOT, 'birth-check-' + str(time.time_ns()))
        out.mkdir(parents=True)
        candidate = out/'values.jsonl'
        env = dict(os.environ, PARTICLES_PROBE_OUT=str(candidate), PARTICLES_IMAGE_DIR=str(out/'images'))
        env.setdefault('DISPLAY', ':0')
        cli = os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
        command = [str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                   'timeout', '900', cli, 'run', '-p', str(ROOT), '--frames', '60',
                   '--size', '320x180', '--no-watch']
        with (out/'engine.log').open('w') as log:
            code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
        if code:
            print('engine failed:', code, out/'engine.log')
            return 1
    a, b = compare.load(reference), compare.load(candidate)
    selected = lambda key: key[0] in {'sub_emitters_birth', 'sub_emitters_birth_child'}
    keys = sorted(k for k in a.keys() | b.keys() if selected(k))
    if not keys:
        raise ValueError('reference has no Birth records')
    bad = 0
    for key in keys:
        ra, rb = a.get(key), b.get(key)
        problems = ['missing reference or candidate'] if ra is None or rb is None else compare.compare(key[0], ra, rb)
        if problems:
            bad += 1
            print(key, problems, 'reference', ra, 'candidate', rb)
    print(f'{len(keys) - bad}/{len(keys)} original Birth records match')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
