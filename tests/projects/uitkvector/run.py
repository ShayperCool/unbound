#!/usr/bin/env python3
"""VectorImage assets of com.unity.vectorgraphics drawn by UI Toolkit; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'VectorRunner.Record', 'UITK_VECTOR_OUT',
                              tolerance=2e-5, engine_args=['--frames', '10', '--size', '1024x640']))
