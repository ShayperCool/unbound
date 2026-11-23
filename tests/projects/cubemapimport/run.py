#!/usr/bin/env python3
"""Cubemaps imported from images by generateCubemap mode and image aspect; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'animatorapi', ('Packages', 'ProjectSettings', 'Assets/Scenes', 'Assets/Scenes.meta'))

if __name__ == '__main__':
    sys.exit(value_probe.main(
        PROBE,
        'CubemapImportProbe.Record',
        'CUBEMAP_IMPORT_OUT',
        # A face texel interpolated from the image may round to the next integer.
        tolerance=1,
        engine_args=['--headless', '600'],
        base=BASE,
        macos_batch=True,
    ))
