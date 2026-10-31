#!/usr/bin/env python3
"""Full sampler observation union, using the clone control's unchanged float tolerances."""
import argparse
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def load(path):
    result = {}
    for line in path.read_text().splitlines():
        row = json.loads(line)
        assert row['k'] not in result, row['k']
        for i, value in enumerate(row['v']):
            result[row['k'] + '/' + str(i)] = value
        result[row['k'] + '/length'] = len(row['v'])
    return result


def compare(a, b):
    values, failures = {}, {}
    for key in sorted(a.keys() | b.keys()):
        tolerance = 0 if key.endswith('/length') else 1e-6 if key.startswith('state/') else 0.002
        row = dict(reference=a.get(key), candidate=b.get(key), tolerance=tolerance,
                   missing=key not in b, extra=key not in a)
        values[key] = row
        if key not in a or key not in b or not math.isfinite(a[key]) or not math.isfinite(b[key]) or abs(a[key] - b[key]) > tolerance:
            failures[key] = row
    return dict(observations=len(values), matched=len(values) - len(failures), failed=len(failures), failures=failures, values=values)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--before', type=Path)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    reference = load(ROOT/'Reference'/('unity-' + args.release)/'values.jsonl')
    after = compare(reference, load(args.candidate))
    report = dict(after=after)
    if args.before:
        before = compare(reference, load(args.before))
        old, new = before['failures'], after['failures']
        report.update(before=before, resolved={k: old[k] for k in old.keys() - new.keys()},
                      introduced={k: new[k] for k in new.keys() - old.keys()},
                      retained={k: dict(before=old[k], after=new[k]) for k in old.keys() & new.keys()})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: v for k, v in after.items() if k not in ('values', 'failures')}))
    raise SystemExit(bool(after['failed']))


if __name__ == '__main__':
    main()
