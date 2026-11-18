#!/usr/bin/env python3
"""Editor tooling APIs 2D content relies on (texture generation, PSD import, material serialized
properties, USS var() types); see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ToolApisProbe.Record', 'TOOL_APIS_OUT',
                              tolerance=0, engine_args=['--headless', '120'], macos_batch=True))
