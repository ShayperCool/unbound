#!/usr/bin/env python3
"""Sprite atlas secondary textures: what a packed sprite reports and how it is lit; see README.md."""
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
        'SpriteAtlasSecondaryProbe.Record',
        'SPRITE_ATLAS_SECONDARY_OUT',
        tolerance=1,
        engine_args=['--frames', '600', '--size', '64x64'],
        base=BASE,
        macos_batch=True,
    ))
