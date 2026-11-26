#!/usr/bin/env python3
"""Joint projection and the configurable joint's frame options; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'JOINTPROJECTION_OUT',
                              tolerance=1e-3, engine_args=['--frames', '400', '--size', '96x64'], macos_batch=True))
