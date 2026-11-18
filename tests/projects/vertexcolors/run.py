#!/usr/bin/env python3
"""The colors renderers put in their vertices, in both color spaces; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'VERTEX_COLORS_OUT',
                              tolerance=1e-5, engine_args=['--frames', '40', '--size', '64x64'], batch=False,
                              # The capture imports the render pipeline package.
                              record_timeout=1800, color_spaces=('Gamma', 'Linear')))
