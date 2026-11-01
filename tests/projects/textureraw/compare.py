#!/usr/bin/env python3
"""Exact comparison retaining the complete missing/extra/failing union."""
import argparse
import json
from pathlib import Path


def load(path):
    rows = {}
    for line in path.read_text().splitlines():
        row = json.loads(line)
        if row['k'] in rows:
            raise ValueError('duplicate key: '+row['k'])
        rows[row['k']] = row['v']
    if rows.get('done') != 'ok':
        raise ValueError('incomplete capture: '+str(path))
    return rows


def compare(reference, candidate):
    a, b = load(reference), load(candidate)
    return {'reference':str(reference), 'candidate':str(candidate),
            'reference_count':len(a), 'candidate_count':len(b),
            'missing':[{'key':k, 'expected':a[k]} for k in sorted(a.keys()-b.keys())],
            'extra':[{'key':k, 'actual':b[k]} for k in sorted(b.keys()-a.keys())],
            'failing':[{'key':k, 'expected':a[k], 'actual':b[k]} for k in sorted(a.keys()&b.keys()) if a[k] != b[k]]}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    result = compare(args.reference, args.candidate)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    with args.report.open('x') as f:
        json.dump(result, f, indent=2)
    counts = {k:len(result[k]) for k in ('missing', 'extra', 'failing')}
    print(json.dumps(counts))
    raise SystemExit(int(any(counts.values())))
