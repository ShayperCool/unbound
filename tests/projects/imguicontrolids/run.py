#!/usr/bin/env python3
"""Measure IMGUI control id numbering in Editor Play mode: permanent ids, ids across passes and
frames, appended and inserted controls, and a second component's OnGUI."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'IMGUI_CONTROL_IDS_OUT', tolerance=0,
                              engine_args=['--frames', '120', '--size', '640x520', '--focus', '1:on'], batch=False, record_timeout=180,
                              base=(PROBE.parent/'imguiskin', BASE)))
