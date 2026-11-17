#!/usr/bin/env python3
"""ImmediateModeElement drawing on a runtime panel; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'UitkImmediateRunner.Record', 'UITK_IMMEDIATE_OUT',
                              tolerance=0, engine_args=['--frames', '120', '--size', '800x600'], macos_batch=True))
