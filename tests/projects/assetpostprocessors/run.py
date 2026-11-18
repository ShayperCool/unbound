#!/usr/bin/env python3
"""AssetPostprocessor callbacks and what they change in imported textures, models and audio; see
README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
# The models come from the model import probe (one authored copy in the repository); this probe
# gives them its own .meta files.
BASE = (PROBE.parent/'modelimport', ['Assets/Models/Materials.fbx', 'Assets/Models/Animated.fbx'])

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'PostprocessorProbe.Record', 'ASSET_POSTPROCESSORS_OUT',
                              tolerance=0, engine_args=['--headless', '600'], macos_batch=True, base=BASE))
