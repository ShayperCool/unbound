#!/usr/bin/env python3
"""Errors from invalid block-compressed Texture2D sizes, see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'RecordCreationErrors.Record', 'TEXTURE_CREATION_ERRORS_OUT',
                              tolerance=0, engine_args=['--headless', '8'], macos_batch=True,
                              color_spaces=('Gamma', 'Linear'), base=BASE))
