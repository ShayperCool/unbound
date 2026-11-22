#!/usr/bin/env python3
"""FileUtil's copy, move and replace calls and its path helpers; see README.md."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'fileutildelete', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'FileUtilCopyRunner.Record', 'FILEUTIL_COPY_OUT',
                              tolerance=0, engine_args=['--headless', '120'], base=BASE, macos_batch=True))
