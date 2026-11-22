#!/usr/bin/env python3
"""Record the actual script frame in direct and inherited Unity messages."""
import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
CAPTURE = 'STACK_MESSAGES_OUT'


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
    if len(roots) < 2 or len(set(roots)) != len(roots):
        raise SystemExit('publish requires at least two distinct Editor runs')
    launches = set()
    for root in roots:
        capture = root/'capture'
        name = value_probe.editor_runner.RESULT
        files = [capture, capture/name, capture/value_probe.VALUES, root/name]
        if any(path.resolve() != path for path in files):
            raise SystemExit(f'{root}: capture files must stay in their run directory')
        metadata = capture/name
        if metadata.exists() and metadata.stat().st_nlink != 1:
            raise SystemExit(f'{metadata}: capture metadata must have a single filesystem link')
        launch = (root/name).read_bytes()
        if launch in launches:
            raise SystemExit(f'{root}: duplicate Editor launch record')
        launches.add(launch)
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
    out = directory(args.label or private_label())
    if out.exists():
        raise SystemExit(f'{out}: expected a fresh run directory')
    if args.mode == 'record':
        value_probe.record(PROBE, 'RecordStackMessages.Record', CAPTURE, str(out), batch=True,
                           macos_batch=True, base=BASE)
        return 0
    return value_probe.gate(PROBE, CAPTURE, str(out), 0, ['--headless', '8'], base=BASE)


if __name__ == '__main__':
    sys.exit(main())
