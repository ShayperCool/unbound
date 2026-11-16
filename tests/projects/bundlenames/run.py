#!/usr/bin/env python3
"""How AssetBundle asset names are looked up; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'BUNDLE_NAMES_OUT',
                              tolerance=0, engine_args=['--frames', '10', '--size', '64x64']))
