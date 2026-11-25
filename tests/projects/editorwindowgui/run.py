#!/usr/bin/env python3
"""Measure EditorGUIUtility icons/cursor rects, EditorWindow.rootVisualElement, Handles draw helpers,
and ReorderableList DoList/DoLayoutList in Editor Play mode."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'EDITOR_WINDOW_GUI_OUT', tolerance=0,
                              engine_args=['--frames', '60', '--size', '480x240'], batch=False, record_timeout=120,
                              base=(PROBE.parent/'imguiskin', BASE)))
