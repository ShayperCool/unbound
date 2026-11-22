#!/usr/bin/env python3
"""Terrain.normalmapTexture: when a terrain has it, what it is and its texels; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'TERRAIN_NORMALMAP_OUT',
                              tolerance=5, engine_args=['--frames', '10', '--size', '64x64']))
