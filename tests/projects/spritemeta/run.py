#!/usr/bin/env python3
"""The .meta files of sprites and of new textures in a 2D project after their first import; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'SPRITEMETA_OUT',
                              tolerance=0, engine_args=['--frames', '10', '--size', '96x64'], macos_batch=True))
