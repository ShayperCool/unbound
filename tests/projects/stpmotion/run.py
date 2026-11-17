#!/usr/bin/env python3
"""STP upscaling with a still and a moving camera; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'STP_MOTION_OUT',
                              tolerance=0, engine_args=['--frames', '600', '--size', '320x180'], batch=False))
