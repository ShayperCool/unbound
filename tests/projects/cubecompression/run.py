#!/usr/bin/env python3
"""Every texel of compressed and uncompressed cubemaps, sampled on the GPU; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'CUBE_COMPRESSION_OUT',
                              tolerance=1e-6, engine_args=['--frames', '10', '--size', '160x90'], metal=True))
