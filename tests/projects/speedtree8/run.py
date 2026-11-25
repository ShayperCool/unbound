#!/usr/bin/env python3
"""What importing SpeedTree 8 trees gives; see README.md.

The trees are not part of the repository: every run stages them into its private project copy
from $UNBOUND_SPEEDTREE_SAMPLES, else $XDG_CACHE_HOME/unbound/speedtree-samples (~/.cache by
default), and stops when they are not there.

    python3 tests/projects/speedtree8/run.py record --label A   # Editor capture (macOS batch is fine)
    python3 tests/projects/speedtree8/run.py publish A B        # two identical captures
    python3 tests/projects/speedtree8/run.py gate               # the engine against Reference/
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

VARIABLE = 'SPEEDTREE8_PROBE_OUT'
# The SpeedTree 8 samples the probe imports (each with its .meta, maps and materials as shipped):
# the files of the samples' v8/ folder, staged under Assets/Trees/v8/.
SAMPLE_SET = 'v8'


def samples():
    root = os.environ.get('UNBOUND_SPEEDTREE_SAMPLES')
    if not root:
        cache = os.environ.get('XDG_CACHE_HOME') or str(Path.home()/'.cache')
        root = str(Path(cache)/'unbound'/'speedtree-samples')
    source = Path(root)/SAMPLE_SET
    if not source.is_dir():
        raise SystemExit(f'speedtree8: the SpeedTree sample trees are not staged: no {source} '
                         '(set UNBOUND_SPEEDTREE_SAMPLES to a directory with v8/; they are never part of the repository)')
    return source


def stage(project):
    source = samples()
    target = project/'Assets'/'Trees'/SAMPLE_SET
    shutil.copytree(source, target)
    # The probe compares nothing of the staged files but what importing them gives.
    print(f'staged {sum(1 for p in target.rglob("*") if p.is_file())} sample files from {source}')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        samples()
        return value_probe.record(PROBE, 'ProbeRunner.Record', VARIABLE, label, True, macos_batch=True, configure=stage)
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    samples()
    return value_probe.gate(PROBE, VARIABLE, label, 0, ['--frames', '10', '--size', '64x64'], configure=stage)


if __name__ == '__main__':
    sys.exit(main())
