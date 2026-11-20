#!/usr/bin/env python3
"""Capture colored rectangles drawn by EditorGUI.DrawRect in Play mode."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    probe = Path(__file__).resolve().parent
    base = (probe.parent/'imguiskin', ('Assets/Scenes', 'Packages', 'ProjectSettings'))
    sys.exit(value_probe.main(probe, 'EditorDrawRectRunner.Record', 'EDITOR_DRAW_RECT_OUT',
                              tolerance=0, engine_args=['--frames', '60', '--size', '320x180'],
                              batch=False, base=base, color_spaces=('Gamma', 'Linear')))
