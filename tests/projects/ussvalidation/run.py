#!/usr/bin/env python3
"""USS values outside their syntax; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'UssValidationRunner.Record', 'USS_VALIDATION_OUT',
                              tolerance=0, engine_args=['--headless', '300'], macos_batch=True))
