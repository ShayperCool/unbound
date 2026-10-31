#!/usr/bin/env python3
"""Replay combined query/skin integration captures into an external workspace."""
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('query_gate_replay', ROOT.parent/'query2dcontinuation/validate.py')
replay = importlib.util.module_from_spec(spec)
spec.loader.exec_module(replay)

if __name__ == '__main__':
    raise SystemExit(replay.main(ROOT, motion=True))
