#!/usr/bin/env python3
"""Verify explicit motion captures; optional baseline preserves every retained failure."""
import importlib.util
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent/'trigger2dtiming'))
spec = importlib.util.spec_from_file_location('timing_verify', ROOT.parent/'trigger2dtiming/verify.py')
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)
from probe_paths import reference_file

if __name__ == '__main__':
    raise SystemExit(verify.verify_suites(ROOT, {'motion':'motion.json.gz', 'rotation':'rotation.json.gz', 'skin':reference_file(ROOT.parent/'colliderskin2d')},
                                         'MOTION_WORK', prefixes={'motion':'', 'rotation':'rotation-', 'skin':'skin-'}))
