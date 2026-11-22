#!/usr/bin/env python3
"""Record and compare a Unity 6 InspectorElement: the default inspector of objects, serialized
objects, components and several objects, custom editors with CreateInspectorGUI or only IMGUI,
the owned editor's lifetime, rebinding, FillDefaultInspector, and the panel's visual tree updater
run one phase at a time."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'InspectorElementRunner.Record', 'INSPECTOR_ELEMENT_OUT',
                              tolerance=0, engine_args=['--headless', '60'], base=BASE, macos_batch=True))
