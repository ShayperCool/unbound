#!/usr/bin/env python3
"""Measure IMGUI.Controls.SearchField, the internal EditorGUI/EditorGUILayout search fields and the
Editor skin styles EditorStyles names, in Editor Play mode: outside OnGUI, in Layout and Repaint,
and through scripted mouse, key and command events."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'EDITOR_SEARCH_FIELDS_OUT', tolerance=0,
                              engine_args=['--frames', '240', '--size', '640x520', '--focus', '1:on'], batch=False, record_timeout=180,
                              base=(PROBE.parent/'imguiskin', BASE)))
