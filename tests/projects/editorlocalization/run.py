#!/usr/bin/env python3
"""What the Editor's interface language settings answer in Play mode; see README.md.

    run.py record --label L [--profile P]   one windowed Editor capture
    run.py publish A B [--profile P]        two identical captures -> Reference/unity-<release>/[<profile>/]
    run.py gate [--profile P]               the engine vs Reference/, every profile or the one named
"""
import argparse
import os
from pathlib import Path
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

VARIABLE = 'EDITOR_LOCALIZATION_OUT'
ENGINE_ARGS = ['--frames', '10', '--size', '96x64']
KEY = 'Editor.kEnableEditorLocalization'
# profile: the EditorPrefs the Editor and the engine see (the machine's are left out: a package may
# have written the key); None, the reference in Reference/unity-<release>/, has none.
PROFILES = {None: {}, 'one': {KEY: 1}, 'zero': {KEY: 0}}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--profile', choices=[p for p in PROFILES if p])
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
    if args.mode == 'record':
        # Windowed (on the private display), as Unbound is one (Application.isBatchMode false).
        prefs = PROFILES[args.profile]
        return value_probe.record(PROBE, 'ProbeRunner.Record', VARIABLE, label, False,
                                  editor_env=lambda out: value_probe.private_editor_home(out, prefs))
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels, profile=args.profile)
    codes = []
    for profile in ([args.profile] if args.profile else list(PROFILES)):
        prefs = PROFILES[profile]
        codes.append(value_probe.gate(PROBE, VARIABLE, f'{label}-{profile or "absent"}', 0, ENGINE_ARGS, profile=profile,
                                      engine_env=lambda out, prefs=prefs: value_probe.private_editor_prefs(out, prefs)))
    return max(codes)


if __name__ == '__main__':
    sys.exit(main())
