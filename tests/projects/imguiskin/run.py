#!/usr/bin/env python3
"""The skin GUI.skin answers in Play mode (styles, font, textures); see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ImguiSkinRunner.Record', 'IMGUI_SKIN_OUT',
                              tolerance=1e-6, engine_args=['--frames', '30', '--size', '320x180'], batch=False))
