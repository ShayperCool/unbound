#!/usr/bin/env python3
"""Record and compare Unity 6 numeric fields bound to a property of several objects when their text
is an expression with per-object variables (L(), R(), +=, *=) or without: each object's value, the
field's text and mixed state, and the undo record."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uitkbindings', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'MultiExprRunner.Record', 'MULTI_EXPR_OUT',
                              tolerance=0, engine_args=['--headless', '6000'], base=BASE, macos_batch=True))
