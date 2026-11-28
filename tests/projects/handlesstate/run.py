#!/usr/bin/env python3
"""Record and check Handles state and HandleUtility line geometry."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
import value_probe

BASE = (ROOT.parent / 'cameramatrices', ('Assets/Scenes', 'Assets/Scenes.meta', 'Assets/Editor', 'Assets/Editor.meta', 'Packages', 'ProjectSettings'))

if __name__ == '__main__':
    sys.exit(value_probe.main(ROOT, 'ProbeRunner.Record', 'HANDLES_STATE_OUT', tolerance=0,
                              engine_args=['--headless', '10'], base=BASE))
