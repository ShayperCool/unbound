#!/usr/bin/env python3
"""Compare every raw event, retaining missing/extra rows and all recorded metadata."""
import argparse
from collections import Counter
import importlib.util
import json
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('reference', type=Path)
p.add_argument('candidate', type=Path)
p.add_argument('--report', type=Path, required=True)
a = p.parse_args()
spec = importlib.util.spec_from_file_location('line_compare', Path(__file__).resolve().parents[1]/'lines/compare.py')
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)

def load(path):
    counts = Counter()
    result = {}
    order = []
    for line in path.read_text().splitlines():
        row = json.loads(line)
        base = json.dumps([row.get(x) for x in ('kind', 'name', 'step', 'phase')], separators=(',', ':'))
        key = base + '#' + str(counts[base])
        counts[base] += 1
        result[key] = row
        order.append(key)
    assert result[order[-1]] == {'complete': True}
    return result, order

ref, ro = load(a.reference)
cap, co = load(a.candidate)
rows = []
for key in sorted(set(ref) | set(cap)):
    difference = comparison.differ(ref[key], cap[key]) if key in ref and key in cap else None
    status = 'extra' if key not in ref else 'missing' if key not in cap else 'fail' if difference else 'pass'
    item = dict(key=key, status=status)
    if key in ref: item['reference'] = ref[key]
    if key in cap: item['actual'] = cap[key]
    if difference: item['difference'] = list(difference[:2])
    rows.append(item)
counts = Counter(x['status'] for x in rows)
a.report.write_text(json.dumps(dict(counts=counts, rows=rows, reference_order=ro, candidate_order=co,
    tolerances=dict(absolute=comparison.ABS, relative=comparison.REL), normalization=None), indent=2)+'\n')
print(dict(counts))
raise SystemExit(int(any(x['status'] != 'pass' for x in rows) or ro != co))
