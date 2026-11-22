#!/usr/bin/env python3
"""Animation remainder probe: BlendTree motion stats, Animation.SyncLayer, obsolete
Animation.cullingType coercion, AnimationMode.GetCurveBindings / AddPropertyModification,
PropertyModificationToEditorCurveBinding, EditorCurveBindingsToGenericBindings, and
AnimatorControllerPlayable.IsParameterControlledByCurve; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (
    PROBE.parent / 'animatorapi',
    (
        'Packages',
        'ProjectSettings',
        'Assets/Scenes',
        'Assets/Clips',
        'Assets/Scripts/ProbeTarget.cs',
        'Assets/Scripts/ProbeTarget.cs.meta',
    ),
)

if __name__ == '__main__':
    sys.exit(value_probe.main(
        PROBE,
        'AnimRestProbe.Record',
        'ANIM_REST_OUT',
        tolerance=1e-4,
        engine_args=['--headless', '3000'],
        base=BASE,
        macos_batch=True,
    ))
