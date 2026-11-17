#!/usr/bin/env python3
"""Visual Effect Graph events wired straight to an Initialize context; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'VFX_PROBE_OUT',
                              tolerance=0, engine_args=['--frames', '80', '--size', '64x64'], batch=False, authored='default',
                              # The capture imports the render pipeline and authors the graphs.
                              record_timeout=1800))
