#!/usr/bin/env python3
"""Record and compare UnityEditor.U2D.SpriteAtlasAsset behavior in Unity 6000.0."""
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
VARIABLE = 'SPRITE_ATLAS_ASSET_OUT'


def configure(project):
    path = project/'ProjectSettings/EditorSettings.asset'
    settings, changed = re.subn(r'(\n  m_SpritePackerMode:) \d+\n', r'\1 5\n', path.read_text())
    if changed != 1:
        raise ValueError('cannot select Sprite Atlas V2 mode in EditorSettings')
    path.write_text(settings)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    args = parser.parse_args()
    if any(Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    if args.mode == 'record':
        value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, args.label, batch=True,
                           base=BASE, macos_batch=True, configure=configure)
        return 0
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    return value_probe.gate(PROBE, VARIABLE, args.label, 0, ['--headless', '120'],
                            base=BASE, configure=configure)


if __name__ == '__main__':
    sys.exit(main())
