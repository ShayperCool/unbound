#!/usr/bin/env python3
"""Record and compare what Editor services answer in Play mode (Unity 6000.0)."""
import argparse
from pathlib import Path
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]
VARIABLE = 'EDITOR_CHANNELS_OUT'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    args = parser.parse_args()
    # One value differs between the references (localization.enableEditorLocalization, which the
    # EditorPrefs decide; the macOS answer without the key is unmeasured): on macOS arm64 the probe
    # records, publishes and gates against the macOS Editor's reference (metal/). The Linux Editor
    # and the engine see EditorPrefs without the key, whatever the machine's hold.
    value_probe.METAL = value_probe.MACOS_ARM64
    if any(Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    if args.mode == 'record':
        private_home = None if value_probe.MACOS_ARM64 else (lambda out: value_probe.private_editor_home(out, {}))
        value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, args.label, batch=True, base=BASE, macos_batch=True,
                           editor_env=private_home)
        return 0
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    return value_probe.gate(PROBE, VARIABLE, args.label, 0, ['--headless', '120'], base=BASE,
                            engine_env=lambda out: value_probe.private_editor_prefs(out, {}))


if __name__ == '__main__':
    sys.exit(main())
