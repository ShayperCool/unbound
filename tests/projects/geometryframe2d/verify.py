#!/usr/bin/env python3
"""Keep every union-key failure and original comparator tolerance in cache reports."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, validation_cache
from query_references import comparator

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--unity-version', required=True)
p.add_argument('--actual', type=Path, required=True)
p.add_argument('--baseline', type=Path)
p.add_argument('--report', type=Path, required=True)
a = p.parse_args()
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.unity_version
if not a.report.resolve().is_relative_to(validation_cache().resolve()):
    p.error('--report must be under the unbound cache')
paths = dict(reference=reference_file(ROOT), actual=a.actual)
if a.baseline:
    paths['baseline'] = a.baseline
data = {k: json.loads(v.read_text()) for k, v in paths.items()}
r = comparator.compare(data['reference'], data['actual'])
# Exact geometry differences remain visible even below the broad comparator tolerance.
r['exact_differences'] = [dict(scenario=s, key=k, reference=data['reference'].get(s, {}).get(k, '<missing>'),
    candidate=data['actual'].get(s, {}).get(k, '<missing>'))
    for s in sorted(data['reference'].keys() | data['actual'].keys())
    for k in sorted(data['reference'].get(s, {}).keys() | data['actual'].get(s, {}).keys())
    if data['reference'].get(s, {}).get(k, '<missing>') != data['actual'].get(s, {}).get(k, '<missing>')]
if a.baseline:
    before = comparator.compare(data['reference'], data['baseline'])
    keys = lambda report: {(v['scenario'], v['key']) for v in report['failures']}
    old, new = keys(before), keys(r)
    r['before'] = before
    r['vs_baseline'] = dict(resolved=sorted(old-new), retained=sorted(old&new), introduced=sorted(new-old))
r['sha256'] = {k: hashlib.sha256(v.read_bytes()).hexdigest() for k, v in paths.items()}
a.report.parent.mkdir(parents=True, exist_ok=True)
a.report.write_text(json.dumps(r, indent=2) + '\n')
print({k:len(v) if isinstance(v, list) else v for k,v in r.items() if k not in ['before', 'sha256', 'vs_baseline']})
if a.baseline:
    print({k:len(v) for k,v in r['vs_baseline'].items()})
raise SystemExit(bool(r['vs_baseline']['introduced'] if a.baseline else r['failures']))
