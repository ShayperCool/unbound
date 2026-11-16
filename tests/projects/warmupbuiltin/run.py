#!/usr/bin/env python3
"""Warm-up of a variant collection listing built-in shaders; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'WARMUP_BUILTIN_OUT',
                              tolerance=0, engine_args=['--frames', '10', '--size', '64x64']))
