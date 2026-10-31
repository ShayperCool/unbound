#!/usr/bin/env python3
"""Verify lifecycle and receiver captures without historical runtimes or artifacts."""
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('timing_verify', ROOT.parent/'trigger2dtiming/verify.py')
verify = importlib.util.module_from_spec(spec)
# The timing verifier imports its adjacent report comparator.
import sys
sys.path.insert(0, str(ROOT.parent/'trigger2dtiming'))
spec.loader.exec_module(verify)

if __name__ == '__main__':
    raise SystemExit(verify.verify_suites(ROOT, {'lifecycle':'lifecycle.json', 'receivers':'receivers.json'},
                                         ('TRIGGER_WORK', 'LIFECYCLE_WORK'), exact=('lifecycle', 'receivers')))
