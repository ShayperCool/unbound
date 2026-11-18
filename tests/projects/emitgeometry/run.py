#!/usr/bin/env python3
"""UI geometry a scriptable pipeline draws per camera type, with and without
ScriptableRenderContext.EmitGeometryForCamera / EmitWorldGeometryForSceneView; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    # Colours are bytes of blended draws: two apart at most between devices' blending.
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'EMIT_GEOMETRY_OUT',
                              tolerance=2, engine_args=['--frames', '10', '--size', '64x64'], devices=('opengl', 'vulkan')))
