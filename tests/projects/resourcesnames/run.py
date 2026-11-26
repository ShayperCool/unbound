#!/usr/bin/env python3
"""Resources paths naming several files; see README.md."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ProbeRunner.Record', 'RESOURCES_NAMES_OUT', 0, ['--frames', '10', '--size', '96x64']))
