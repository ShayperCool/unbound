#!/usr/bin/env python3
"""Record selected asset paths and framing without a Project window in Unity 6000.0."""
import argparse
from pathlib import Path
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]
VARIABLE = 'SELECTED_ASSET_PATH_OUT'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    args = parser.parse_args()
    if any(Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    if args.mode == 'record':
        value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, args.label, batch=True, base=BASE, macos_batch=True)
        value_probe.seal_value_capture(value_probe.run_root(PROBE, args.label).resolve())
        return 0
    if args.mode == 'publish':
        value_probe.verify_sealed_value_repeats(
            [value_probe.run_root(PROBE, label).resolve() for label in args.labels])
        return value_probe.publish(PROBE, args.labels)
    return value_probe.gate(PROBE, VARIABLE, args.label, 0, ['--headless', '120'], base=BASE)


if __name__ == '__main__':
    sys.exit(main())
