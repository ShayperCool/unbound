#!/usr/bin/env python3
"""IMGUI border widths and radiuses, alpha textures of canvas draws and sprites; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'UI_ALPHA_BORDERS_OUT',
                              tolerance=0, engine_args=['--frames', '60', '--size', '320x180', '--target', 'Android'],
                              batch=False))
