#!/usr/bin/env python3
"""Record and compare the Unity 6 editor toolbar controls (UnityEditor.UIElements Toolbar,
ToolbarButton, ToolbarToggle, ToolbarMenu, ToolbarSpacer, ToolbarSearchField,
ToolbarPopupSearchField, ToolbarBreadcrumbs): structure, USS classes, style sheets, values,
events and UXML creation."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ToolbarControlsRunner.Record', 'TOOLBAR_CONTROLS_OUT',
                              tolerance=0, engine_args=['--headless', '60'], base=BASE, macos_batch=True))
