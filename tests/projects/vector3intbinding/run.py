#!/usr/bin/env python3
"""Record and compare Unity 6 Vector3Int SerializedProperty binding to Vector3IntField and PropertyField."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'Vector3IntBindingRunner.Record', 'VECTOR3INT_BINDING_OUT',
                              tolerance=0, engine_args=['--headless', '60'], base=BASE))
