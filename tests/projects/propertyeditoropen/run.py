#!/usr/bin/env python3
"""Record focused inspector opening in Unity or compare Unbound."""
import argparse
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402
from probe_paths import private_label  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]
VARIABLE = 'PROPERTY_EDITOR_OPEN_OUT'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=private_label())
    args = parser.parse_args()
    if any(Path(label).name != label or label in ('.', '..') for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    profile = 'macos' if sys.platform == 'darwin' else None
    if args.mode == 'record':
        editor_env = (lambda out: value_probe.private_editor_home(out, {})) if profile is None else None
        value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, args.label, batch=True, base=BASE,
                           macos_batch=profile == 'macos', editor_env=editor_env)
        return 0
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels, profile=profile)
    residuals = 'residuals-macos.json' if profile == 'macos' else 'residuals.json'
    return value_probe.gate(PROBE, VARIABLE, args.label, 0, ['--headless', '120'], base=BASE,
                            profile=profile, residuals_file=residuals)


if __name__ == '__main__':
    sys.exit(main())
