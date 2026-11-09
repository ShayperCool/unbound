#!/usr/bin/env python3
"""Replay the URP temporal-upscale scenario and retain its existing image comparison."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
PROBE = HERE.parent/'urp'
REFERENCE_RELEASE = '6000.0.71f1'
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import run_root


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True)
    parser.add_argument('--cli', default=os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND'))
    parser.add_argument('--label', default='stp-'+uuid.uuid4().hex[:12])
    args = parser.parse_args()
    if not args.cli: parser.error('--cli or UNBOUND is required')
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    # The scenario set predates versioned directories; keep its recorded release explicit.
    if args.release != REFERENCE_RELEASE:
        parser.error('the existing URP scenario reference is recorded for '+REFERENCE_RELEASE)
    work = run_root(PROBE, args.label)
    project = work/'project'
    if work.exists(): parser.error('refusing to overwrite '+str(work))
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, project/part)
    output = work/'out'
    output.mkdir()
    env = dict(os.environ, URP_PROBE_DIR=str(output), UNBOUND_PROBE_UNITY_VERSION=args.release)
    launcher = Path.home()/'.cache/compat/with-heavy-job'
    command = [str(launcher), '--kind', 'engine-gpu', 'timeout', '1200', args.cli, 'run', '-p', str(project),
               '--no-watch', '--frames', '60', '--size', '320x180']
    # Preserve the scenario's preceding renders and history; compare only the STP mechanism.
    with (work/'engine.log').open('w') as log:
        result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode: return result.returncode
    candidate = output/'upscale_stp.png'
    if not candidate.is_file():
        print('missing STP capture:', candidate)
        return 1
    result = subprocess.run([sys.executable, str(PROBE/'compare.py'),
        str(PROBE/'Reference/scenarios/upscale_stp.png'), str(candidate)], capture_output=True, text=True)
    (work/'comparison.log').write_text(result.stdout+result.stderr)
    print(result.stdout, end='')
    if result.stderr: print(result.stderr, file=sys.stderr, end='')
    print('STP capture:', candidate)
    return result.returncode


if __name__ == '__main__':
    sys.exit(main())
