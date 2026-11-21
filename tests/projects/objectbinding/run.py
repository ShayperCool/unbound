#!/usr/bin/env python3
"""Record and compare Unity 6 ObjectReference SerializedProperty binding to ObjectField and PropertyField."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ObjectBindingRunner.Record', 'OBJECT_BINDING_OUT',
                              tolerance=0, engine_args=['--headless', '60'], base=BASE))
