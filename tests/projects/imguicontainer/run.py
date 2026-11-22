#!/usr/bin/env python3
"""Record and compare a Unity 6 IMGUIContainer in a runtime panel: the size it measures from its
IMGUI layout, the events its handler gets each frame and their coordinates, a mouse event sent to
it, and a PropertyField drawing a drawer's IMGUI."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiContainerRunner.Record', 'IMGUI_CONTAINER_OUT',
                              tolerance=0, engine_args=['--frames', '600', '--size', '800x600'], base=BASE, macos_batch=True))
