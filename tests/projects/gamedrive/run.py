#!/usr/bin/env python3
"""The game driver (tools/gamedrive) run on a small UI in the Editor and in the engine; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
# The driver's package, as a game's clone holds it: embedded in Packages/.
DRIVER = (PROBE.parents[2]/'tools/gamedrive', ['Packages/com.unbound.gamedrive'])

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'GameDriveProbe.Record', 'GAMEDRIVE_PROBE_OUT', tolerance=0,
                              engine_args=['--headless', '1000000'], macos_batch=True, base=DRIVER))
