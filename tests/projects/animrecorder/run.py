#!/usr/bin/env python3
"""Record and compare what the Recorder editor's GameObjectRecorder answers in Play mode (Unity 6000.0)."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

# Keys are JSON numbers: single-precision arithmetic of the recorded inputs (Quaternion.Euler,
# sines) and of curve evaluation differs in the last bits, which a slope over a short frame
# magnifies (the largest measured: 1.6e-4 on an Euler angle of 120 degrees). Key counts, binding
# lists, tangent modes and every string compare exactly.
TOLERANCE = 2e-4

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'ANIM_RECORDER_OUT', tolerance=TOLERANCE,
                              engine_args=['--headless', '120'], base=BASE, macos_batch=True))
