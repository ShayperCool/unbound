#!/usr/bin/env python3
"""What shaders sample from default and built-in textures in a linear project; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'DEFAULT_TEXTURES_OUT',
                              tolerance=0.002, engine_args=['--frames', '10', '--size', '64x64']))
