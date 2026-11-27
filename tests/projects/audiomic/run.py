#!/usr/bin/env python3
"""Measure UnityEngine.Microphone enumeration, capabilities, validation, and recording."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Editor', 'Assets/Scenes', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    if sys.platform != 'linux':
        mode = sys.argv[1] if len(sys.argv) > 1 else ''
        if mode == 'gate':
            print('audiomic: Microphone capture is implemented on Linux PulseAudio only; not run on this OS')
            sys.exit(0)
        raise SystemExit('audiomic: Microphone recording probe runs on Linux only')
    sys.exit(value_probe.main(
        PROBE, 'ProbeRunner.Record', 'AUDIO_MIC_OUT',
        tolerance=0.05,
        engine_args=['--frames', '20000', '--size', '320x180'],
        batch=True,
        record_timeout=180,
        base=(PROBE.parent / 'video', BASE),
    ))
