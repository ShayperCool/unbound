#!/usr/bin/env python3
"""Measure EditorGUILayout groups, spacing, and EditorGUIUtility helpers in Editor Play mode."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'EDITOR_LAYOUT_OUT', tolerance=0,
                              engine_args=['--frames', '60', '--size', '480x240'], batch=False, record_timeout=100,
                              base=(PROBE.parent/'imguiskin', BASE)))
