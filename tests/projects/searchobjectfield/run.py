#!/usr/bin/env python3
"""UnityEditor.Search.ObjectField outside a panel: structure, defaults, display, values and UXML data; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'animatorapi', ('ProjectSettings', 'Assets/Scenes', 'Assets/Scenes.meta'))

if __name__ == '__main__':
    sys.exit(value_probe.main(
        PROBE,
        'SearchObjectFieldProbe.Record',
        'SEARCH_OBJECT_FIELD_OUT',
        tolerance=0,
        engine_args=['--headless', '300'],
        base=BASE,
        macos_batch=True,
    ))
