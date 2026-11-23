#!/usr/bin/env python3
"""Discrete curves of engine components' properties; see README.md."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkpanelutils', ('Assets/Scenes',)),
        (PROBE.parent/'modelroot', ('ProjectSettings',))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'EngineDiscreteRunner.Record', 'ENGINE_DISCRETE_OUT',
                              tolerance=0, engine_args=['--frames', '30', '--size', '640x480'], base=BASE, macos_batch=True))
