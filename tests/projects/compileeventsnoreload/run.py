#!/usr/bin/env python3
"""The compile events probe with Enter Play Mode Options keeping the domain; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

HERE = Path(__file__).resolve().parent
BASE = (HERE.parent/'compileevents', ['Assets', 'Packages', 'ProjectSettings'])

if __name__ == '__main__':
    sys.exit(value_probe.main(HERE, 'CompileEventsProbe.Record', 'COMPILE_EVENTS_OUT',
                              tolerance=0, engine_args=['--headless', '6000', '--watch'], macos_batch=True, base=BASE))
