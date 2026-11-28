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

# The exact Unbound outcomes of the two macOS controls whose prerequisites differ.
MACOS_FOCUS_FALLBACKS = {
    'open.priorWindowFocus': 'before=prior|afterOpen=prior|openEvents=prior|afterClose=prior|closeEvents=prior|lostFocus=0',
    'open.closeAfterFocusChanged': 'hadFocus=True|focusedAfter=True|events=1',
}


def macos_focus_gate(label):
    reference = value_probe.reference_dir(PROBE, profile='macos') / value_probe.VALUES
    capture = value_probe.run_root(PROBE, label) / 'engine' / value_probe.VALUES
    expected = value_probe.read_values(reference)
    actual = value_probe.read_values(capture)
    unsupported = [key for key, fallback in MACOS_FOCUS_FALLBACKS.items()
                   if actual.get(key) not in (expected[key], fallback)]
    for key in unsupported:
        print(f'{key}: result outside the recorded macOS focus residual')
    return int(bool(unsupported))


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
    result = value_probe.gate(PROBE, VARIABLE, args.label, 0, ['--headless', '120'], base=BASE,
                              profile=profile, residuals_file=residuals)
    return result if result or profile is None else macos_focus_gate(args.label)


if __name__ == '__main__':
    sys.exit(main())
