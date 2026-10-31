#!/usr/bin/env python3
"""Complete ordered union comparison; only physical value arrays use the original tolerances."""
import argparse
import json
import math
from pathlib import Path

MISSING = object()


def compare(reference, candidate):
    a = [json.loads(line) for line in Path(reference).read_text().splitlines()]
    b = [json.loads(line) for line in Path(candidate).read_text().splitlines()]
    failures = []
    count = 0
    def visit(x, y, key, physical=False):
        nonlocal count
        if x is MISSING or y is MISSING:
            count += 1
            failures.append({'key': key, 'reference': {'missing': True} if x is MISSING else x, 'candidate': {'missing': True} if y is MISSING else y})
        elif isinstance(x, dict) and isinstance(y, dict):
            for k in sorted(x.keys() | y.keys()):
                visit(x.get(k, MISSING), y.get(k, MISSING), key + '/' + k, k == 'values')
        elif isinstance(x, list) and isinstance(y, list):
            for i in range(max(len(x), len(y))):
                visit(x[i] if i < len(x) else MISSING, y[i] if i < len(y) else MISSING, key + '/' + str(i), physical)
        else:
            count += 1
            same = math.isclose(x, y, rel_tol=2e-6, abs_tol=2e-5) if physical and type(x) in (int, float) and type(y) in (int, float) else x == y
            if not same:
                failures.append({'key': key, 'reference': x, 'candidate': y})
    visit(a, b, '')
    return {'samples': len(a), 'callbacks': sum(sum(not e['kind'].startswith('after-') for e in r['events']) for r in a),
            'observations': count, 'matched': count-len(failures), 'failed': len(failures), 'failures': failures}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('reference', type=Path)
    p.add_argument('candidate', type=Path)
    p.add_argument('--report', type=Path)
    a = p.parse_args()
    report = compare(a.reference, a.candidate)
    if a.report:
        a.report.parent.mkdir(parents=True, exist_ok=True)
        a.report.write_text(json.dumps(report, indent=2) + '\n')
    else:
        for failure in report['failures']: print(json.dumps(failure))
    print(json.dumps({k:v for k,v in report.items() if k != 'failures'}))
    return int(report['failed'] != 0)


if __name__ == '__main__':
    raise SystemExit(main())
