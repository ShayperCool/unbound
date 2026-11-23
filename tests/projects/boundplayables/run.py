#!/usr/bin/env python3
"""Animator.hasBoundPlayables through an Animator's life; see README.md."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

MODELS = tuple(f'Assets/Resources/{name}{ext}' for name in ('WalkerNoNode.fbx', 'C_WalkerNoNode_Loop.controller')
               for ext in ('', '.meta'))
BASE = [(PROBE.parent/'uitkpanelutils', ('Assets/Scenes',)),
        (PROBE.parent/'modelroot', ('Packages', 'ProjectSettings') + MODELS)]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'BoundPlayablesRunner.Record', 'BOUND_PLAYABLES_OUT',
                              tolerance=0, engine_args=['--frames', '30', '--size', '640x480'], base=BASE, macos_batch=True))
