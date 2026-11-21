#!/usr/bin/env python3
"""Record and compare Unity 6 SerializedProperty and binding controls: enum name order, nicified
names with digit-x-digit parts, multi-object Vector4 edits, usage drawers on collection elements
and the timing of a usage drawer's change events."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'BindingControlsRunner.Record', 'BINDING_CONTROLS_OUT',
                              tolerance=0, engine_args=['--headless', '60'], base=BASE, macos_batch=True))
