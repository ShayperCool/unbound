#!/usr/bin/env python3
"""Camera.Render() with the URP 2D renderer into a target texture; see README.md."""
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
        'CameraRender2DProbe.Record',
        'CAMERA_RENDER_2D_OUT',
        tolerance=0,
        engine_args=['--frames', '600', '--size', '64x64'],
        base=BASE,
        macos_batch=True,
    ))
