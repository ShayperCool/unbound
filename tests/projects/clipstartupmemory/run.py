#!/usr/bin/env python3
"""Profile the first script-created clip in fresh Unity and Unbound sessions."""
import argparse
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent / 'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
PROFILES = ('PCM', 'Callback', 'Stream', 'Reset', 'ResetOwner')
CAPTURE = 'CLIP_STARTUP_OUT'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--profile', choices=PROFILES)
    parser.add_argument('--label', default=f'run-{os.getpid()}')
    args = parser.parse_intermixed_args()
    if args.mode != 'gate' and not args.profile:
        parser.error(f'{args.mode} requires --profile')
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels, profile=args.profile)

    statuses = []
    for profile in (args.profile,) if args.profile else PROFILES:
        os.environ['CLIP_STARTUP_PROFILE'] = profile
        label = f'{args.label}-{profile}'
        if args.mode == 'record':
            return value_probe.record(PROBE, 'RecordStartupMemory.Record', CAPTURE, label, batch=True,
                                      macos_batch=True, base=BASE)
        statuses.append(value_probe.gate(PROBE, CAPTURE, label, 0, ['--headless', '12'], base=BASE,
                                         profile=profile))
    return max(statuses)


if __name__ == '__main__':
    sys.exit(main())
