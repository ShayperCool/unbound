#!/usr/bin/env python3
"""Measure EditorGUI/EditorGUILayout rendering layer mask fields in Editor Play mode: outside
OnGUI and in Layout and Repaint, with three named rendering layers."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'EDITOR_RENDERING_LAYERS_OUT', tolerance=0,
                              engine_args=['--frames', '240', '--size', '640x520', '--focus', '1:on'], batch=False, record_timeout=180,
                              base=(PROBE.parent/'imguiskin', BASE)))
