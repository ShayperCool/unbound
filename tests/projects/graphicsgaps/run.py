#!/usr/bin/env python3
"""Graphics members render pipelines, packages and editor tools reach at run time; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
# The URP project of tests/projects/rpglobalsettings: its packages, project settings, scene,
# global settings and volume profile; this probe's settings, shaders and scripts over them.
BASE = (PROBE.parent/'rpglobalsettings', ['Packages', 'ProjectSettings', 'Assets/Scenes', 'Assets/Scenes.meta',
                                          'Assets/Settings.meta', 'Assets/UniversalRenderPipelineGlobalSettings.asset',
                                          'Assets/UniversalRenderPipelineGlobalSettings.asset.meta',
                                          'Assets/DefaultVolumeProfile.asset', 'Assets/DefaultVolumeProfile.asset.meta'])

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'ProbeRunner.Record', 'GRAPHICS_GAPS_OUT', tolerance=0,
                              engine_args=['--frames', '10', '--size', '192x108'], batch=False, base=BASE, devices=('opengl', 'vulkan')))
