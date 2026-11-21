#!/usr/bin/env python3
"""Record and compare multi-atlas runtime sprite binding across V1 and V2 Sprite Packer modes."""
import argparse
from pathlib import Path
import re
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('Packages', 'ProjectSettings')),
        (PROBE.parent/'spriteatlaseditor', ('Assets/Sprites',))]
VARIABLE = 'SPRITE_ATLAS_MULTIBIND_OUT'
PROFILES = {
    'v1': (4, '.spriteatlas'),
    'v2': (5, '.spriteatlasv2'),
}


def configure(project, profile):
    mode, kept = PROFILES[profile]
    path = project/'ProjectSettings/EditorSettings.asset'
    settings, changed = re.subn(r'(\n  m_SpritePackerMode:) \d+\n', rf'\1 {mode}\n', path.read_text())
    if changed != 1:
        raise ValueError('cannot select Sprite Packer mode in EditorSettings')
    path.write_text(settings)
    for atlas in (project/'Assets/Atlases').iterdir():
        name = atlas.name[:-len('.meta')] if atlas.name.endswith('.meta') else atlas.name
        if Path(name).suffix != kept:
            atlas.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    parser.add_argument('--profile', choices=PROFILES)
    args = parser.parse_args()
    if any(Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    selected = [args.profile] if args.profile else PROFILES
    codes = []
    for profile in selected:
        def setup(project, profile=profile):
            configure(project, profile)

        label = f'{args.label}-{profile}'
        if args.mode == 'record':
            value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, label, batch=True,
                               base=BASE, macos_batch=True, configure=setup)
        elif args.mode == 'publish':
            codes.append(value_probe.publish(PROBE, [f'{name}-{profile}' for name in args.labels], profile=profile))
        else:
            codes.append(value_probe.gate(PROBE, VARIABLE, label, 0, ['--headless', '120'],
                                          base=BASE, configure=setup, profile=profile))
    return max(codes, default=0)


if __name__ == '__main__':
    sys.exit(main())
