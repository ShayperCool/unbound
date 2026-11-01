#!/usr/bin/env python3
"""Retain the full boundary union with the original particle-control tolerances."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('trigger_verify', ROOT.parent/'particletrigger/verify.py')
trigger = importlib.util.module_from_spec(spec)
spec.loader.exec_module(trigger)
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--unity-version', required=True)
p.add_argument('--native-a', type=Path)
p.add_argument('--native-b', type=Path)
p.add_argument('--actual', type=Path)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
if bool(a.native_a) != bool(a.native_b): p.error('Provide both independent Native recordings')
trigger.cache_artifact_path(a.output)
canonical = ROOT/'Reference'/('unity-'+a.unity_version+'.jsonl')
reference = a.native_a or canonical
ref = trigger.load(reference)
result = {}
paths = [reference]
if a.native_a:
    paths.append(a.native_b)
    result['exact_native_agreement'] = a.native_a.read_bytes() == a.native_b.read_bytes()
    result['native'] = trigger.report(ref, trigger.load(a.native_b))
if canonical.exists():
    paths.append(canonical)
    result['canonical_agrees_exactly'] = canonical.read_bytes() == reference.read_bytes()
if a.actual:
    paths.append(a.actual)
    result['actual'] = trigger.report(ref, trigger.load(a.actual))
result['sha256'] = {str(f): hashlib.sha256(f.read_bytes()).hexdigest() for f in paths}
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
print({k: {n:v for n,v in r.items() if n != 'failures'} if isinstance(r, dict) else r for k,r in result.items() if k != 'sha256'})
raise SystemExit(int(not result.get('exact_native_agreement', True) or not result.get('canonical_agrees_exactly', True)
                     or bool(result.get('actual', {}).get('failures'))))
