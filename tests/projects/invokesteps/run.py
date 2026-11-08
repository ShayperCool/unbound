#!/usr/bin/env python3
"""Fixed-step Invoke clocks and sub-frame repeating calls; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'InvokeStepsRunner.Record', 'INVOKE_STEPS_OUT',
                              tolerance=0, engine_args=['--frames', '400', '--size', '160x90'], batch=False))
