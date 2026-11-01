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
p.add_argument('--rotation-inputs', action='store_true', help='Use quaternion-derived body rotation controls')
p.add_argument('--unattached-statics', action='store_true', help='Use shared static-body and proxy lifetime controls')
p.add_argument('--manifold-controls', action='store_true', help='Use near-parallel cast and physical manifold controls')
p.add_argument('--transform-inputs', action='store_true', help='Use public frame and parenting controls')
p.add_argument('--capsule-geometry', action='store_true', help='Use capsule construction boundary controls')
p.add_argument('--contact-inputs', action='store_true', help='Use the unchanged indexed query controls')
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
try:
    cache_artifact_path(a.output)
except ValueError as error:
    p.error(str(error))
if sum([a.enumeration, a.velocities, a.lifetimes, a.rotation_inputs, a.unattached_statics, a.contact_inputs, a.capsule_geometry, a.transform_inputs, a.manifold_controls]) > 1: p.error('Select one control set')
ref = a.reference or ROOT/'Reference'/('unity-'+a.release)/('manifolds.json' if a.manifold_controls else 'transform-inputs.json' if a.transform_inputs else 'capsule-geometry.json' if a.capsule_geometry else 'unattached-statics.json' if a.unattached_statics else 'rotation-inputs.json' if a.rotation_inputs else 'lifetimes.json' if a.lifetimes else 'velocities.json' if a.velocities else 'enumeration.json.gz' if a.enumeration else 'public.json')
if a.contact_inputs and not a.reference: ref = ROOT.parent/'query2dties/Reference'/('unity-'+a.release+'.json')
tolerance = 0 if a.exact else 1e-3
reference = c.load(ref)
candidate = c.load(a.candidate)
result = c.compare(reference, candidate, tolerance)
result['newly_missing'] = []
if a.baseline:
    baseline = c.load(a.baseline)
    result = c.changes(c.compare(reference, baseline, tolerance), result)
    # Losing an observation is a regression even if its earlier numeric value failed.
    result['newly_missing'] = [
        {'scenario': scenario, 'key': key, 'reference': expected,
         'baseline': baseline[scenario][key]}
        for scenario, values in reference.items() for key, expected in values.items()
        if key in baseline.get(scenario, {}) and key not in candidate.get(scenario, {})]
result['inputs_sha256'] = {str(f.resolve()):c.sha(f) for f in [ref, a.candidate]+([a.baseline] if a.baseline else [])}
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
c.describe('contact pair order', result)
raise SystemExit(bool(result['newly_missing'] or
                      (result['introduced'] if a.baseline else result['failures'])))
