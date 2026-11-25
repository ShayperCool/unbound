#!/usr/bin/env python3
"""Measure the pixels an ObjectIdRequest writes into sRGB and linear targets in Gamma and Linear
projects (the URP project of tests/projects/graphicsgaps)."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'rpglobalsettings', ['Packages', 'ProjectSettings', 'Assets/Scenes', 'Assets/Scenes.meta',
                                           'Assets/Settings.meta', 'Assets/UniversalRenderPipelineGlobalSettings.asset',
                                           'Assets/UniversalRenderPipelineGlobalSettings.asset.meta',
                                           'Assets/DefaultVolumeProfile.asset', 'Assets/DefaultVolumeProfile.asset.meta']),
        (PROBE.parent/'graphicsgaps', ['ProjectSettings', 'Assets/Settings', 'Assets/Editor', 'Assets/Editor.meta'])]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ProbeRunner.Record', 'OBJECT_ID_SPACE_OUT', tolerance=0,
                              engine_args=['--frames', '10', '--size', '192x108'], batch=False, base=BASE,
                              color_spaces=('Gamma', 'Linear')))
