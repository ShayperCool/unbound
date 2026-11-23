#!/usr/bin/env python3
"""Which sprite the 2D renderer draws on top when a SortingGroup's layer differs from its child's; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'urp2d', ('Packages', 'ProjectSettings', 'Assets/Settings', 'Assets/Settings.meta',
                              'Assets/UniversalRenderPipelineGlobalSettings.asset', 'Assets/UniversalRenderPipelineGlobalSettings.asset.meta',
                              'Assets/DefaultVolumeProfile.asset', 'Assets/DefaultVolumeProfile.asset.meta'))

if __name__ == '__main__':
    sys.exit(value_probe.main(
        PROBE,
        'SortingGroup2DProbe.Record',
        'SORTING_GROUP_2D_OUT',
        tolerance=0,
        engine_args=['--frames', '600', '--size', '64x64'],
        base=BASE,
        macos_batch=True,
    ))
