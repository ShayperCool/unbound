#!/usr/bin/env python3
"""Legacy TextMesh output (mesh, bounds, materials, logs, images); see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'TextMeshRunner.Record', 'TEXTMESH_PROBE_OUT',
                              tolerance=1e-4, engine_args=['--frames', '60', '--size', '320x180']))
