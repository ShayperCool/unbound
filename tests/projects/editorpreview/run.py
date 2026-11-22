#!/usr/bin/env python3
"""Record and compare Unity 6 object previews: ObjectPreview's defaults and target walking, an
Editor's preview members, DrawPreview's calls for one and several targets, and
Editor.DrawPropertiesExcluding."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'EditorPreviewRunner.Record', 'EDITOR_PREVIEW_OUT',
                              tolerance=0, engine_args=['--frames', '600', '--size', '800x600'], base=BASE, macos_batch=True))
