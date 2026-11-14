#!/usr/bin/env python3
"""Record or replay duplicate-component logging controls."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from value_probe import main

if __name__ == '__main__':
    sys.exit(main(PROBE, 'ProbeRunner.Record', 'COMPONENT_LOGS_OUT', 0,
                  ['--scene', 'Assets/Scenes/Probe.unity', '--headless', '300', '--fixed-fps', '60']))
