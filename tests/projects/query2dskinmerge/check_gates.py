#!/usr/bin/env python3
"""Verify combined query/skin integration captures using canonical Unity references."""
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('query_integration_gates', ROOT.parent/'query2dcontinuation/check_gates.py')
gates = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gates)

if __name__ == '__main__':
    raise SystemExit(gates.main(ROOT, combined=True))
