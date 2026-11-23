#!/usr/bin/env python3
"""The Input System mouse at the start of Play mode, the pointer not moved; see README.md."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkpanelutils', ('Assets/Scenes',)),
        (PROBE.parent/'input', ('Packages', 'ProjectSettings', 'Assets/InputSystem.inputsettings.asset',
                                'Assets/InputSystem.inputsettings.asset.meta'))]

if __name__ == '__main__':
    # Windowed: a batch-mode Editor has no pointer. The engine runs in a window too. The host OS
    # decides the values: on macOS arm64 the reference is the macOS Editor's (Reference/.../metal/,
    # recorded windowed), elsewhere the Linux Editor's.
    sys.exit(value_probe.main(PROBE, 'InitialPointerRunner.Record', 'INITIAL_POINTER_OUT', tolerance=0,
                              engine_args=['--frames', '30', '--size', '960x540'], batch=False, base=BASE, metal=True))
