#!/usr/bin/env python3
"""Record and compare the Undo records of SerializedObject.ApplyModifiedProperties in Unity 6: their
group and name, what undo and redo restore, applies without changes or without Undo, several
applies in one group or in several, several targets and a component."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'SerializedUndoRunner.Record', 'SERIALIZED_UNDO_OUT',
                              tolerance=0, engine_args=['--headless', '60'], base=BASE, macos_batch=True))
