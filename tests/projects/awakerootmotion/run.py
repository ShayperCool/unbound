#!/usr/bin/env python3
"""An exception in Awake disables the script; Animator.hasRootMotion of generic and humanoid rigs;
see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
RESOURCES = 'Assets/Resources/'
# The rigs and controllers of the root motion and humanoid probes (one authored copy each).
MODELROOT = [RESOURCES+name+suffix for name in ('Walker.fbx', 'WalkerNoNode.fbx', 'WalkerNoAvatar.fbx', 'Mover.fbx',
             'MoverAvatar.fbx', 'C_Walker_Full.controller', 'C_WalkerNoNode_Full.controller',
             'C_WalkerNoAvatar_Full.controller', 'C_Mover_Full.controller', 'C_MoverAvatar_Full.controller')
             for suffix in ('', '.meta')]
HUMANOID = [RESOURCES+name+suffix for name in ('HumanA.fbx', 'HumanA.controller') for suffix in ('', '.meta')]
BASE = [(PROBE.parent/'modelroot', MODELROOT), (PROBE.parent/'humanoid', HUMANOID)]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'AwakeRootMotionProbe.Record', 'AWAKE_ROOT_MOTION_OUT',
                              tolerance=0, engine_args=['--headless', '600'], macos_batch=True, base=BASE))
