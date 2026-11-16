#!/usr/bin/env python3
"""Light probe proxy volumes over tests/projects/lightprobes' baked Main scene; see README.md.

    run.py record --label L     one batchmode GPU Editor capture (OpenGL Core)
    run.py publish A B          two byte-identical captures -> Reference/unity-<release>/
    run.py gate [--label L]     Unbound on a fresh project, compared with Reference/
"""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

# Texels and constants are floats the Editor stores as 32-bit (and 16-bit for HalfFloat volumes).
TOLERANCE = 2e-4
# The baked scenes, the Editor's runner (opens Main, enters Play mode) and the settings of
# tests/projects/lightprobes; not its runtime script.
BASE = ('Assets/Scenes', 'Assets/Scenes.meta', 'Assets/Editor', 'Assets/Editor.meta', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ProbeRunner.Record', 'LPPV_OUT', tolerance=TOLERANCE,
                              engine_args=['--force-glcore', '--frames', '30', '--size', '160x120'],
                              base=(PROBE.parent/'lightprobes', BASE)))
