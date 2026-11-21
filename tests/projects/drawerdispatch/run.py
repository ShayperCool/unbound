#!/usr/bin/env python3
"""Record and compare how a Unity 6 PropertyField dispatches to property drawers and decorator
drawers: [CustomPropertyDrawer] lookup by type, base type, interface, generic type and attribute,
attribute order and nesting, the IMGUI fallback, decorators, collections and the built-in drawers."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'DrawerDispatchRunner.Record', 'DRAWER_DISPATCH_OUT',
                              tolerance=0, engine_args=['--headless', '60'], base=BASE, macos_batch=True))
