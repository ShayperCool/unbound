#!/usr/bin/env python3
"""Measure UnityEngine.Microphone enumeration, capabilities, validation, and recording."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Editor', 'Assets/Scenes', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(
        PROBE, 'ProbeRunner.Record', 'AUDIO_MIC_OUT',
        tolerance=0.05,
        engine_args=['--frames', '20000', '--size', '320x180'],
        batch=True,
        record_timeout=180,
        base=(PROBE.parent / 'video', BASE),
    ))
