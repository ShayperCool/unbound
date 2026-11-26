#!/usr/bin/env python3
"""Measure EditorGUI/EditorGUILayout.InspectorTitlebar in
Editor Play mode: outside OnGUI, in Layout and Repaint, and through scripted mouse/key events."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'EDITOR_TITLEBAR_OUT', tolerance=0,
                              engine_args=['--frames', '240', '--size', '320x180', '--focus', '1:on'], batch=False, record_timeout=180,
                              base=(PROBE.parent/'imguiskin', BASE)))
