#!/usr/bin/env python3
"""The Editor DCC model formats Unbound implements; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'DCCMODELS_OUT', tolerance=1e-4,
                              engine_args=['--frames', '10', '--size', '96x64'], macos_batch=True))
