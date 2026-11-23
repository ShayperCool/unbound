#!/usr/bin/env python3
"""Percentages in UI Toolkit layout across layout passes; see README.md."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkpanelutils', ('Assets/Resources', 'Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'UitkPercentRunner.Record', 'UITK_PERCENT_OUT',
                              tolerance=0, engine_args=['--frames', '60', '--size', '640x480'], base=BASE, macos_batch=True))
