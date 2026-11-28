#!/usr/bin/env python3
"""Record selected asset paths and framing without a Project window in Unity 6000.0."""
import argparse
import json
from pathlib import Path
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402
from editor_runner import RESULT  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]
VARIABLE = 'SELECTED_ASSET_PATH_OUT'


def publish(labels, profile):
    roots = [value_probe.run_root(PROBE, label).resolve() for label in labels]
    value_probe.verify_sealed_value_repeats(roots)
    expected_device = 'metal' if profile == 'macos' else None
    for root in roots:
        launch = json.loads((root / RESULT).read_text())
        if launch.get('device') != expected_device:
            raise SystemExit(f'{root.name}: Editor launch does not match the {profile} profile')
    return value_probe.publish(PROBE, labels, profile=profile)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    args = parser.parse_args()
    if any(not label or label in ('.', '..') or Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    profile = 'macos' if sys.platform == 'darwin' else 'linux'
    if args.mode == 'record':
        value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, args.label, batch=True, base=BASE, macos_batch=True)
        value_probe.seal_value_capture(value_probe.run_root(PROBE, args.label).resolve())
        return 0
    if args.mode == 'publish':
        return publish(args.labels, profile)
    return value_probe.gate(PROBE, VARIABLE, args.label, 0, ['--headless', '120'], base=BASE, profile=profile)


if __name__ == '__main__':
    sys.exit(main())
