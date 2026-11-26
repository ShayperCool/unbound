#!/usr/bin/env python3
"""What the Editor's interface language settings answer in Play mode; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    # Recorded in a windowed Editor (on the private display): Unbound is the windowed Editor
    # (Application.isBatchMode false), and the batch-mode Editor answers otherwise.
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'EDITOR_LOCALIZATION_OUT',
                              tolerance=0, engine_args=['--frames', '10', '--size', '96x64'], batch=False))
