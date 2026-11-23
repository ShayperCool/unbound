#!/usr/bin/env python3
"""TransformAccessArray capacity and length; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'animatorapi', ('Packages', 'ProjectSettings', 'Assets/Scenes', 'Assets/Scenes.meta'))

if __name__ == '__main__':
    sys.exit(value_probe.main(
        PROBE,
        'TransformAccessProbe.Record',
        'TRANSFORM_ACCESS_OUT',
        tolerance=0,
        engine_args=['--headless', '600'],
        base=BASE,
        macos_batch=True,
    ))
