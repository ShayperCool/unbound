#!/usr/bin/env python3
"""Which reflection probes a scriptable pipeline's culling makes visible (Unity 6000.0); see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'REFL_PROBE_CULL_OUT', tolerance=0,
                              engine_args=['--frames', '200', '--size', '64x64'], base=BASE))
