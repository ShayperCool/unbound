#!/usr/bin/env python3
"""The SpeedTree 8 wind constants trees draw with, frame by frame; see README.md.

The sample trees are not part of the repository: every run stages them into its private project
copy from $UNBOUND_SPEEDTREE_SAMPLES, else $XDG_CACHE_HOME/unbound/speedtree-samples (~/.cache by
default), and stops when they are not there.

    python3 tests/projects/speedtree8wind/run.py record --label A   # Editor capture (Linux, GPU)
    python3 tests/projects/speedtree8wind/run.py publish A B        # two identical captures
    python3 tests/projects/speedtree8wind/run.py gate               # the engine against Reference/
    python3 tests/projects/speedtree8wind/run.py gate --if-staged   # the same, a SKIPPED line without the samples
"""
import argparse
import os
from pathlib import Path
import shutil
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

VARIABLE = 'SPEEDTREE_PROBE_OUT'
SAMPLE_SET = 'v8'


def samples():
    root = os.environ.get('UNBOUND_SPEEDTREE_SAMPLES')
    if not root:
        cache = os.environ.get('XDG_CACHE_HOME') or str(Path.home()/'.cache')
        root = str(Path(cache)/'unbound'/'speedtree-samples')
    source = Path(root)/SAMPLE_SET
    if not source.is_dir():
        raise SystemExit(f'speedtree8wind: the SpeedTree sample trees are not staged: no {source} '
                         '(set UNBOUND_SPEEDTREE_SAMPLES to a directory with v8/; they are never part of the repository)')
    return source


def stage(project):
    source = samples()
    target = project/'Assets'/'Trees'/SAMPLE_SET
    shutil.copytree(source, target)
    print(f'staged {sum(1 for p in target.rglob("*") if p.is_file())} sample files from {source}')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    parser.add_argument('--if-staged', action='store_true',
                        help='gate only where the samples are staged; elsewhere print SKIPPED and succeed')
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        samples()
        return value_probe.record(PROBE, 'ProbeRunner.Record', VARIABLE, label, True, configure=stage)
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    try:
        samples()
    except SystemExit as absent:
        if not args.if_staged:
            raise
        print(f'speedtree8wind: SKIPPED, nothing compared: {absent}')
        return 0
    return value_probe.gate(PROBE, VARIABLE, label, 0, ['--frames', '1700', '--size', '64x64'], configure=stage)


if __name__ == '__main__':
    sys.exit(main())
