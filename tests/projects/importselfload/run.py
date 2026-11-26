#!/usr/bin/env python3
"""What loading an asset gives from its own import, and which importer a .meta naming another one gives; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'IMPORTSELFLOAD_OUT',
                              tolerance=0, engine_args=['--frames', '10', '--size', '96x64'], macos_batch=True))
