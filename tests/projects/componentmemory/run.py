#!/usr/bin/env python3
"""Record native allocation sizes of components with a fixed per-object cost."""
import argparse
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
CAPTURE = 'COMPONENT_MEMORY_OUT'


def directory(label):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected a single run directory name')
    base = run_root(PROBE, '').resolve()
    out = base/label
    if out.resolve() != out or inside_git_checkout(out):
        raise SystemExit(f'{out}: run directory must stay in the probe cache outside any checkout')
    return out


def validate_capture(root, release):
    rows = value_probe.read_values(root/'capture'/value_probe.VALUES)
    if rows.get('unity') != release:
        raise SystemExit(f'{root}: expected Editor capture for {release}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--release', required=True)
    parser.add_argument('--label')
    args = parser.parse_intermixed_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.mode == 'publish':
        roots = [directory(label) for label in args.labels]
        value_probe.verify_sealed_value_repeats(roots)
        for root in roots:
            validate_capture(root, args.release)
        return value_probe.publish(PROBE, [str(root) for root in roots])
    if args.labels:
        parser.error('use --label for record and gate')
    out = directory(private_label() if args.label is None else args.label)
    if out.exists():
        raise SystemExit(f'{out}: expected a fresh run directory')
    if args.mode == 'record':
        value_probe.record(PROBE, 'RecordComponentMemory.Record', CAPTURE, str(out),
                           batch=True, macos_batch=True, base=BASE)
        validate_capture(out, args.release)
        value_probe.seal_value_capture(out)
        return 0
    return value_probe.gate(PROBE, CAPTURE, str(out), 0, ['--headless', '8'], base=BASE)


if __name__ == '__main__':
    sys.exit(main())
