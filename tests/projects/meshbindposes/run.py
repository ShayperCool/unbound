#!/usr/bin/env python3
"""Compare the runtime mesh's bind-pose assignment and read-only views."""
import argparse
import os
from pathlib import Path
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))

from probe_paths import run_root  # noqa: E402
import value_probe  # noqa: E402

BASE = (PROBE.parent / 'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
OUTPUT = 'MESH_BINDPOSES_OUT'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('--release', required=True, choices=('6000.0.71f1',))
    parser.add_argument('--label')
    parser.add_argument('labels', nargs='*')
    args = parser.parse_intermixed_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.mode == 'publish':
        value_probe.verify_sealed_value_repeats([run_root(PROBE, name) for name in args.labels])
        return value_probe.publish(PROBE, args.labels)
    if args.labels:
        parser.error('positional labels are only used when publishing')
    label = args.label or time.strftime(f'{args.mode}-%Y%m%d-%H%M%S-') + str(os.getpid())
    if args.mode == 'record':
        value_probe.record(PROBE, 'RecordMeshBindposes.Record', OUTPUT, label,
                           batch=True, macos_batch=True, base=BASE)
        value_probe.seal_value_capture(run_root(PROBE, label))
        return 0
    return value_probe.gate(PROBE, OUTPUT, label, 0, ['--frames', '10', '--size', '64x64'], base=BASE)


if __name__ == '__main__':
    sys.exit(main())
