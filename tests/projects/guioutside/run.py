#!/usr/bin/env python3
"""What IMGUI calls do outside OnGUI; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    # Windowed: the batch-mode Editor calls no OnGUI.
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'GUI_OUTSIDE_OUT',
                              tolerance=0, engine_args=['--frames', '20', '--size', '96x64'], batch=False))
