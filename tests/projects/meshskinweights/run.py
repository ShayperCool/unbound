#!/usr/bin/env python3
"""Compare runtime mesh skin weights and their effect on CPU skinning."""
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
OUTPUT = 'MESH_SKINNING_OUT'
PROFILE = 'MESH_SKINNING_PROFILE'
COPIES = 'copies'


def profiled_label(label, profile):
    return f'{label}-{profile}' if profile else label


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('--release', required=True, choices=('6000.0.71f1',))
    parser.add_argument('--label')
    parser.add_argument('--profile', choices=(COPIES,))
    parser.add_argument('labels', nargs='*')
    args = parser.parse_intermixed_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.mode == 'publish':
        labels = [profiled_label(label, args.profile) for label in args.labels]
        value_probe.verify_sealed_value_repeats([run_root(PROBE, label) for label in labels])
        return value_probe.publish(PROBE, labels, profile=args.profile)
    if args.labels:
        parser.error('positional labels are only used when publishing')
    label = args.label or time.strftime(f'{args.mode}-%Y%m%d-%H%M%S-') + str(os.getpid())
    if args.mode == 'record':
        os.environ[PROFILE] = args.profile or ''
        label = profiled_label(label, args.profile)
        value_probe.record(PROBE, 'RecordMeshSkinning.Record', OUTPUT, label,
                           batch=True, macos_batch=True, base=BASE)
        value_probe.seal_value_capture(run_root(PROBE, label))
        return 0
    profiles = (args.profile,) if args.profile else (None, COPIES)
    results = []
    for profile in profiles:
        os.environ[PROFILE] = profile or ''
        results.append(value_probe.gate(PROBE, OUTPUT, profiled_label(label, profile), 0.000002,
                                        ['--frames', '10', '--size', '64x64'], base=BASE, profile=profile))
    return int(any(results))


if __name__ == '__main__':
    sys.exit(main())
