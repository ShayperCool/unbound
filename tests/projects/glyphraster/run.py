#!/usr/bin/env python3
"""Record and compare legacy dynamic glyph metrics and alpha pixels."""
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe
if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'GLYPH_RASTER_OUT',
                              tolerance=0, engine_args=['--frames', '420', '--size', '32x32']))
