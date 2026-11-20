#!/usr/bin/env python3
"""Compare Inspector IMGUI style state in both Editor themes."""
import argparse
import os
from pathlib import Path
import sys
import time
from unittest.mock import patch

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = (PROBE.parent/'imguiskin', ('Assets/Scenes', 'Packages', 'ProjectSettings'))
VARIABLE = 'EDITOR_STYLES_OUT'
THEMES = ('light', 'dark')


class PrivatePrefsEditorJob(value_probe.editor_runner.EditorJob):
    def command(self):
        prefix = ['bwrap', '--bind', '/', '/', '--dev-bind', '/dev', '/dev']
        for directory in (Path.home()/'.local/share/unity3d', Path.home()/'.config/unity3d'):
            if directory.is_dir():
                prefix.extend(['--overlay-src', str(directory), '--tmp-overlay', str(directory)])
            else:
                prefix.extend(['--tmpfs', str(directory)])
        return prefix + super().command()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    parser.add_argument('--theme', choices=THEMES)
    args = parser.parse_args()
    if any(Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    if args.mode == 'record' and sys.platform != 'linux':
        parser.error('recording both themes uses a private Linux preference overlay')

    codes = []
    for theme in (args.theme,) if args.theme else THEMES:
        label = f'{args.label}-{theme}'
        if args.mode == 'publish':
            codes.append(value_probe.publish(PROBE, [f'{name}-{theme}' for name in args.labels], profile=theme))
            continue
        with patch.dict(os.environ, EDITOR_STYLES_THEME=theme,
                        EDITOR_STYLES_SET_ENGINE_THEME='1' if args.mode == 'gate' else '0'):
            if args.mode == 'record':
                with patch.object(value_probe.editor_runner, 'EditorJob', PrivatePrefsEditorJob):
                    value_probe.record(PROBE, 'EditorStylesRunner.Record', VARIABLE, label, batch=False, base=BASE)
            else:
                engine_args = ['--frames', '60', '--size', '320x180', '--move', '4:160,90', '--focus', '1:on']
                codes.append(value_probe.gate(PROBE, VARIABLE, label, 1e-6, engine_args, base=BASE, profile=theme))
    return max(codes, default=0)


if __name__ == '__main__':
    sys.exit(main())
