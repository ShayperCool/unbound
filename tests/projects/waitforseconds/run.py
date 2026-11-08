#!/usr/bin/env python3
"""When WaitForSeconds, Awaitable.WaitForSecondsAsync and Invoke resume; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'WaitForSecondsRunner.Record', 'WAIT_FOR_SECONDS_OUT',
                              tolerance=0, engine_args=['--frames', '400', '--size', '160x90'], batch=False))
