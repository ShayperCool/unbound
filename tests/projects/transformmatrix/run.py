#!/usr/bin/env python3
"""Record and compare the exact float bits in Transform matrices and local poses."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = (PROBE.parent / 'animatorapi', ('Packages', 'ProjectSettings', 'Assets/Scenes', 'Assets/Scenes.meta'))

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'TransformMatrixProbeRunner.Record', 'TRANSFORM_MATRIX_OUT',
                              tolerance=0, engine_args=['--headless', '30', '--size', '64x64'], base=BASE))
