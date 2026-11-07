#!/usr/bin/env python3
"""Replay the advanced 2D controls without starting the Editor."""
import os
from pathlib import Path
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import run_root

release = os.environ['UNBOUND_PROBE_UNITY_VERSION']
runtime = Path(os.environ['UNBOUND']).resolve().parent
work = run_root(ROOT, 'gate-' + uuid.uuid4().hex)
base = [sys.executable, str(ROOT/'run.py')]
args = ['--release', release, '--work', str(work)]
for mode, extra in [
    ('prepare', ['--label', 'engine']),
    ('engine', ['--label', 'engine', '--runtime', str(runtime)]),
    ('compare', ['--label', 'engine', '--candidate', str(work/'engine.json'), '--output', str(work/'comparison.json')]),
]:
    subprocess.run([*base, mode, *args, *extra], check=True)
