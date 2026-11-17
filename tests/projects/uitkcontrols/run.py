#!/usr/bin/env python3
"""The runtime UI Toolkit controls HelpBox, PopupWindow, ToggleButtonGroup and TwoPaneSplitView; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'UitkControlsRunner.Record', 'UITK_CONTROLS_OUT',
                              tolerance=0, engine_args=['--headless', '300'], macos_batch=True))
