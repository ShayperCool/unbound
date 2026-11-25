#!/usr/bin/env python3
"""Measure EditorGUI/EditorGUILayout colour, object, curve, gradient, mask, layer, tag and enum-flags
fields and their SerializedProperty forms in Editor Play mode: outside OnGUI, in Layout and Repaint,
the menus' choices as commands, and scripted mouse, key and drop events."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'EDITOR_OBJECT_FIELDS_OUT', tolerance=0,
                              engine_args=['--frames', '400', '--size', '640x540', '--focus', '1:on'], batch=False, record_timeout=180,
                              base=(PROBE.parent/'imguiskin', BASE)))
