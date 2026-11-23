#!/usr/bin/env python3
"""Two AssetModificationProcessors with the same editability callbacks; see
tests/projects/openforedit/README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = [(PROBE.parent/'animatorapi', ('ProjectSettings', 'Assets/Scenes', 'Assets/Scenes.meta')),
        (PROBE.parent/'openforedit', ('Assets', 'Packages'))]

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
