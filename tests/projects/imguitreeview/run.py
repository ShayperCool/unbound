#!/usr/bin/env python3
"""Measure an IMGUI TreeView in Editor Play mode: rows, row rects, indents and RowGUI arguments in
Layout and Repaint, its callbacks, and scripted clicks, keys and a search."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'IMGUI_TREE_VIEW_OUT', tolerance=0,
                              engine_args=['--frames', '1500', '--size', '640x480', '--focus', '1:on'], batch=False, record_timeout=180,
                              base=(PROBE.parent/'imguiskin', BASE)))
