#!/usr/bin/env python3
"""Strict ambient records and sun controls, retaining every missing/extra/failing value."""
import argparse
import json
import math
from pathlib import Path

# Same per-coefficient tolerance as the unchanged compare.py.
TOLERANCE = 0.0025
PROBE = Path(__file__).resolve().parent


def records(path):
    result = {}
    for i, line in enumerate(path.read_text().splitlines()):
        value = json.loads(line)
        key = value.get('name', value.get('case'))
        if key in result:
            key = f'{key} duplicate at {i}'
        result[key] = value
    return result


def compare(reference, actual):
    failures, deltas = [], {}
    for key in sorted(reference.keys() | actual.keys()):
        if key not in reference or key not in actual:
            failures.append({'key': key, 'expected': reference.get(key), 'actual': actual.get(key)})
            continue
        a, b = reference[key], actual[key]
        for field in sorted(a.keys() | b.keys()):
            x, y = a.get(field), b.get(field)
            if field in ('probe', 'ambient', 'rendered') and isinstance(x, list) and isinstance(y, list):
                worst = 0.0
                for i in range(max(len(x), len(y))):
                    e = x[i] if i < len(x) else None
                    v = y[i] if i < len(y) else None
                    delta = abs(e-v) if isinstance(e, (int, float)) and isinstance(v, (int, float)) else math.inf
                    worst = max(worst, delta)
                    if not math.isfinite(delta) or delta > TOLERANCE:
                        failures.append({'key': key, 'field': field, 'coefficient': i, 'expected': e, 'actual': v, 'delta': delta})
                deltas[f'{key}/{field}'] = worst
            elif field not in a or field not in b or x != y:
                failures.append({'key': key, 'field': field, 'expected': x, 'actual': y})
    return failures, deltas


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True)
    parser.add_argument('--actual', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    if args.release != '6000.0.71f1':
        parser.error('ambient oracle is measured only on 6000.0.71f1')
    failures, deltas = [], {}
    for group, reference, actual in [
        ('ambient', PROBE/'Reference/ambient.jsonl', args.actual/'ambient/ambient.jsonl'),
        ('sun', PROBE/f'Reference/unity-{args.release}/sun.jsonl', args.actual/'sun/sun.jsonl'),
    ]:
        bad, worst = compare(records(reference), records(actual))
        failures += [dict(group=group, **entry) for entry in bad]
        deltas[group] = worst
    report = {'release': args.release, 'coefficient_tolerance': TOLERANCE,
              'deltas': deltas, 'failures': failures}
    args.report.write_text(json.dumps(report, indent=2)+'\n')
    for failure in failures:
        print(json.dumps(failure))
    for group, values in deltas.items():
        print(f'{group}: {len(values)} coefficient sets, worst {max(values.values()):.9g}')
    print(f'ambient strict gate: {len(failures)} failing values')
    return bool(failures)


if __name__ == '__main__':
    raise SystemExit(main())
