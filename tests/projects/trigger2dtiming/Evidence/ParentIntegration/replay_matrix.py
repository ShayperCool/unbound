#!/usr/bin/env python3
"""Replay the existing matrix into TRIGGER_WORK without replacing delegate evidence."""
import importlib.util
import os
from pathlib import Path

fixture = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('trigger_matrix', fixture / 'matrix.py')
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)

# SCENE and all comparison inputs were resolved from the real fixture on import.
# ROOT is used only for the final export destination in matrix.main().
matrix.ROOT = Path(os.environ['TRIGGER_WORK']) / 'matrix-export'
(matrix.ROOT / 'Evidence').mkdir(parents=True, exist_ok=True)
matrix.main()
