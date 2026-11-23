#!/usr/bin/env python3
"""MakeEditable, CanOpenForEdit and IsMetaFileOpenForEdit without version control; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'animatorapi', ('ProjectSettings', 'Assets/Scenes', 'Assets/Scenes.meta'))

if __name__ == '__main__':
    sys.exit(value_probe.main(
        PROBE,
        'OpenForEditProbe.Record',
        'OPEN_FOR_EDIT_OUT',
        tolerance=0,
        engine_args=['--headless', '300'],
        base=BASE,
        macos_batch=True,
    ))
