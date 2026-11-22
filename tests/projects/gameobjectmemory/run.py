#!/usr/bin/env python3
"""Record component-list memory for new and cloned GameObjects."""
import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root, unity_version  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
CAPTURE = 'GAME_OBJECT_MEMORY_OUT'


def directory(label):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected a single run directory name')
    base = run_root(PROBE, '').resolve()
    out = base/label
    if out.resolve() != out or inside_git_checkout(out):
        raise SystemExit(f'{out}: run directory must stay in the probe cache outside any checkout')
    return out


def publish(labels):
    roots = [directory(label) for label in labels]
    value_probe.verify_sealed_value_repeats(roots)
    for root in roots:
        rows = value_probe.read_values(root/'capture'/value_probe.VALUES)
        if rows.get('unity') != unity_version():
            raise SystemExit(f'{root}: expected Editor capture for {unity_version()}')
    return value_probe.publish(PROBE, [str(root) for root in roots])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    args = parser.parse_intermixed_args()
    if args.mode == 'publish':
        return publish(args.labels)
    if args.labels:
        parser.error('use --label for record and gate')
    out = directory(private_label() if args.label is None else args.label)
    if out.exists():
        raise SystemExit(f'{out}: expected a fresh run directory')
    if args.mode == 'record':
        value_probe.record(PROBE, 'RecordGameObjectMemory.Record', CAPTURE, str(out), batch=True,
                           macos_batch=True, base=BASE)
        value_probe.seal_value_capture(out)
        return 0
    return value_probe.gate(PROBE, CAPTURE, str(out), 0, ['--headless', '8'], base=BASE)


if __name__ == '__main__':
    sys.exit(main())
