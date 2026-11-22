#!/usr/bin/env python3
"""The Editor's GUI skins (EditorGUIUtility.GetBuiltinSkin) and the skin an Editor GUI pass has;
see README.md."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Scenes', 'Assets/Editor', 'Packages', 'ProjectSettings')

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ImguiSkinRunner.Record', 'EDITOR_SKIN_OUT', tolerance=1e-6,
                              engine_args=['--frames', '60', '--size', '480x240'], batch=False, record_timeout=100,
                              base=(PROBE.parent/'imguiskin', BASE)))
