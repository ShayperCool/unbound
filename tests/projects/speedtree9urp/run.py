#!/usr/bin/env python3
"""What importing SpeedTree 9 trees gives under URP 17; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'SPEEDTREE_PROBE_OUT',
                              tolerance=0, engine_args=['--frames', '10', '--size', '64x64']))
