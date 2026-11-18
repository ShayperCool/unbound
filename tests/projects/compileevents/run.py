#!/usr/bin/env python3
"""Compilation events when a script changes in Play mode; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'CompileEventsProbe.Record', 'COMPILE_EVENTS_OUT',
                              tolerance=0, engine_args=['--headless', '6000', '--watch'], macos_batch=True))
