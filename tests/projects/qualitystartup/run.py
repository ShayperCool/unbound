#!/usr/bin/env python3
"""The quality level Play mode starts with; see README.md.

    run.py record --label L [--profile P]   one Editor capture (batch mode)
    run.py publish A B [--profile P]        two identical captures -> Reference/unity-<release>/[<profile>/]
    run.py gate [--profile P]               the engine vs Reference/, every profile or the one named
"""
import argparse
from pathlib import Path
import re
import sys
import time
import os

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

VARIABLE = 'QUALITY_STARTUP_OUT'
ENGINE_ARGS = ['--frames', '10', '--size', '96x64']


def exclude(levels, platform='Standalone', default=None, current=None):
    """A project edit: the quality levels `levels` (indices) exclude `platform`; `default`, when
    given, becomes the Standalone default level, `current` the current level."""
    def edit(project):
        path = project/'ProjectSettings/QualitySettings.asset'
        text = path.read_text()
        parts = text.split('    excludedTargetPlatforms: []\n')
        if len(parts) != 7:
            raise SystemExit(f'{path}: expected six quality levels without excluded platforms')
        out = parts[0]
        for index, part in enumerate(parts[1:]):
            out += f'    excludedTargetPlatforms:\n    - {platform}\n' if index in levels else '    excludedTargetPlatforms: []\n'
            out += part
        if default is not None:
            out, count = re.subn(r'(?m)^    Standalone: \d+$', f'    Standalone: {default}', out)
            if count != 1:
                raise SystemExit(f'{path}: no Standalone default')
        if current is not None:
            out, count = re.subn(r'(?m)^  m_CurrentQuality: \d+$', f'  m_CurrentQuality: {current}', out)
            if count != 1:
                raise SystemExit(f'{path}: no current quality level')
        path.write_text(out)
    return edit


# profile: (Editor arguments, engine arguments, project edit); the reference of None is Reference/unity-<release>/.
PROFILES = {
    None: ((), (), None),
    'android': (('-buildTarget', 'Android'), ('--target', 'Android'), None),
    # The current level (1) and level 3 exclude the active platform; its default (4) does not.
    'excluded': ((), (), exclude({1, 3})),
    # The current level and the Standalone default (3) both exclude Standalone.
    'excludeddefault': ((), (), exclude({1, 3}, default=3)),
    # The current level (3) excluded with included levels before (1, 2) and after (4, 5) it, the
    # first level excluded too and the default the last: which included level replaces it.
    'excludedmiddle': ((), (), exclude({0, 3}, default=5, current=3)),
    # Levels excluded for Standalone with Android active (a control).
    'excludedandroid': (('-buildTarget', 'Android'), ('--target', 'Android'), exclude({1, 3})),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--profile', choices=[p for p in PROFILES if p])
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
    if args.mode == 'record':
        editor_args, _, configure = PROFILES[args.profile]
        return value_probe.record(PROBE, 'ProbeRunner.Record', VARIABLE, label, True,
                                  configure=configure, editor_args=editor_args)
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels, profile=args.profile)
    profiles = [args.profile] if args.profile else list(PROFILES)
    codes = []
    for profile in profiles:
        _, engine_args, configure = PROFILES[profile]
        codes.append(value_probe.gate(PROBE, VARIABLE, f'{label}-{profile or "default"}', 0, [*ENGINE_ARGS, *engine_args],
                                      configure=configure, profile=profile))
    return max(codes)


if __name__ == '__main__':
    sys.exit(main())
