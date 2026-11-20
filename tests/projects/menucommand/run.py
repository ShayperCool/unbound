#!/usr/bin/env python3
"""Record and compare MenuCommand and menu items that take one."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'MENU_COMMAND_OUT', 0, ['--headless', '120'],
                              batch=True, base=BASE, macos_batch=True))
