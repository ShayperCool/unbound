#!/usr/bin/env python3
"""Retain the full union of public observations and each original numeric tolerance."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path
spec = importlib.util.spec_from_file_location('comparison', ROOT.parent/'trigger2dtiming/report.py')
c = importlib.util.module_from_spec(spec)
spec.loader.exec_module(c)
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--candidate', type=Path, required=True)
p.add_argument('--baseline', type=Path)
p.add_argument('--reference', type=Path, help='Independent Native capture when checking repeats')
p.add_argument('--exact', action='store_true', help='Exact comparison for Native repeats')
p.add_argument('--enumeration', action='store_true', help='Use the extended GetContacts reference')
p.add_argument('--velocities', action='store_true', help='Use contact observation and update controls')
p.add_argument('--lifetimes', action='store_true', help='Use simultaneous loss and creation controls')
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
try:
    cache_artifact_path(a.output)
except ValueError as error:
    p.error(str(error))
if sum([a.enumeration, a.velocities, a.lifetimes]) > 1: p.error('Select one control set')
ref = a.reference or ROOT/'Reference'/('unity-'+a.release)/('lifetimes.json' if a.lifetimes else 'velocities.json' if a.velocities else 'enumeration.json.gz' if a.enumeration else 'public.json')
tolerance = 0 if a.exact else 1e-3
reference = c.load(ref)
result = c.compare(reference, c.load(a.candidate), tolerance)
if a.baseline: result = c.changes(c.compare(reference, c.load(a.baseline), tolerance), result)
result['inputs_sha256'] = {str(f.resolve()):c.sha(f) for f in [ref, a.candidate]+([a.baseline] if a.baseline else [])}
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
c.describe('contact pair order', result)
raise SystemExit(bool(result['introduced'] if a.baseline else result['failures']))
