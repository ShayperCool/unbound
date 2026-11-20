#!/usr/bin/env python3
"""Measure programmatic ReorderableList state during Play mode."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'REORDERABLE_LIST_OUT', tolerance=0,
                              engine_args=['--frames', '30', '--size', '160x120'],
                              base=(PROBE.parent/'imguiskin', BASE)))
