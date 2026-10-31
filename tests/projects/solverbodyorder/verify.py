#!/usr/bin/env python3
"""Retain the full union of public observations and each original numeric tolerance."""
import argparse
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('comparison', ROOT.parent/'trigger2dtiming/report.py')
c = importlib.util.module_from_spec(spec)
spec.loader.exec_module(c)
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--candidate', type=Path, required=True)
p.add_argument('--baseline', type=Path)
p.add_argument('--reference', type=Path, help='Independent Native capture when checking repeats')
p.add_argument('--exact', action='store_true', help='Exact comparison for Native repeats')
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
if a.output.resolve().is_relative_to(ROOT.parents[2]): p.error('Reports belong outside Git')
ref = a.reference or ROOT/'Reference'/('unity-'+a.release+'.json')
tolerance = 0 if a.exact else 1e-3
reference = c.load(ref)
result = c.compare(reference, c.load(a.candidate), tolerance)
if a.baseline: result = c.changes(c.compare(reference, c.load(a.baseline), tolerance), result)
result['inputs_sha256'] = {str(f.resolve()):c.sha(f) for f in [ref, a.candidate]+([a.baseline] if a.baseline else [])}
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
c.describe('body lifetime order', result)
raise SystemExit(bool(result['introduced'] if a.baseline else result['failures']))
