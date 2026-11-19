#!/usr/bin/env python3
"""The Editor's animator controller object model, AnimationUtility and AnimationMode answers and
line/trail bounds; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'AnimatorApiProbe.Record', 'ANIMATOR_API_OUT',
                              tolerance=0, engine_args=['--headless', '3000'], macos_batch=True))
