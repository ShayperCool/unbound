#!/usr/bin/env python3
"""Profile the first software-decoded ASTC image and later allocations."""
import argparse
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root, unity_version  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
CAPTURE = 'ASTC_MEMORY_OUT'
PROFILES = ('Ordinary', 'Wide', 'CubeFirst', 'ResizeFirst', 'UninitializedFirst', 'CloneFirst')


def directory(label, profile):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected a single run directory name')
    base = run_root(PROBE, '').resolve()
    out = base/f'{label}-{profile}'
    if out.resolve() != out or inside_git_checkout(out):
        raise SystemExit(f'{out}: run directory must stay in the probe cache outside a checkout')
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--profile', choices=PROFILES, required=True)
    args = parser.parse_intermixed_args()
    if args.mode == 'publish':
        roots = [directory(label, args.profile) for label in args.labels]
        value_probe.verify_sealed_value_repeats(roots)
        for root in roots:
            rows = value_probe.read_values(root/'capture'/value_probe.VALUES)
            if (rows.get('device'), rows.get('unity'), rows.get('profile')) != ('OpenGLCore', unity_version(), args.profile):
                raise SystemExit(f'{root}: unexpected device, version or run profile')
        return value_probe.publish(PROBE, [str(root) for root in roots], profile=args.profile)

    if args.labels:
        parser.error('use --label for record and gate')
    os.environ['ASTC_MEMORY_PROFILE'] = args.profile
    label = private_label() if args.label is None else args.label
    out = directory(label, args.profile)
    if out.exists():
        raise SystemExit(f'{out}: expected a fresh run directory')
    if args.mode == 'record':
        value_probe.record(PROBE, 'RecordAstcMemory.Record', CAPTURE, str(out), batch=True, base=BASE)
        value_probe.seal_value_capture(out)
        return 0
    return value_probe.gate(PROBE, CAPTURE, str(out), 0, ['--headless', '12', '--force-glcore'],
                            base=BASE, profile=args.profile)


if __name__ == '__main__':
    sys.exit(main())
