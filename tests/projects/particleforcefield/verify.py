#!/usr/bin/env python3
"""Verify exact Native agreement and compare a candidate without discarding union keys."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
from compare import load, report

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--unity-version', required=True)
p.add_argument('--native-a', type=Path, required=True)
p.add_argument('--native-b', type=Path, required=True)
p.add_argument('--actual', type=Path)
p.add_argument('--baseline', type=Path)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
try:
    cache_artifact_path(a.output)
except ValueError as error:
    p.error(str(error))
canonical = Path(__file__).parent/'Reference'/('unity-'+a.unity_version+'.jsonl')
inputs = [a.native_a, a.native_b]
result = dict(exact_native_agreement=a.native_a.read_bytes()==a.native_b.read_bytes(),
              native=report(load(a.native_a),load(a.native_b)))
if canonical.exists():
    inputs.append(canonical)
    result['canonical_agrees_exactly'] = canonical.read_bytes()==a.native_a.read_bytes()
if a.actual:
    inputs.append(a.actual)
    result['candidate'] = report(load(a.native_a), load(a.actual))
    if a.baseline:
        inputs.append(a.baseline)
        result['baseline'] = report(load(a.native_a),load(a.baseline))
        before = {(f['key'],f['path']) for f in result['baseline']['failures']}
        after = {(f['key'],f['path']) for f in result['candidate']['failures']}
        result['union'] = {kind: [list(x) for x in sorted(values)] for kind,values in
                           [('resolved',before-after),('retained',before&after),('introduced',after-before)]}
result['sha256'] = {str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in inputs}
a.output.parent.mkdir(parents=True,exist_ok=True)
a.output.write_text(json.dumps(result,indent=2)+'\n')
print('exact Native agreement:',result['exact_native_agreement'])
if 'candidate' in result:
    print({k:v for k,v in result['candidate'].items() if k!='failures'})
if 'union' in result:
    print({k:len(v) for k,v in result['union'].items()})
raise SystemExit(int(not result['exact_native_agreement'] or not result.get('canonical_agrees_exactly',True)
                     or bool(result.get('candidate',{}).get('failures'))))
