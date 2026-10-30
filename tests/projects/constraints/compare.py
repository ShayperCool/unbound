#!/usr/bin/env python3
"""Compare Constraints Editor and Unbound values.jsonl files, recursively (2e-4 tolerance)."""
import json
import math
import sys
from pathlib import Path


def load(directory):
    return {r['k']: r['v'] for r in map(json.loads, (Path(directory) / 'values.jsonl').read_text().splitlines())}


def differences(a, b, path=''):
    if isinstance(a, dict) and isinstance(b, dict):
        for key in a.keys() | b.keys():
            if key not in a or key not in b:
                yield path + '.' + key, a.get(key, 'MISSING'), b.get(key, 'MISSING')
            else:
                yield from differences(a[key], b[key], path + '.' + key)
    elif isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        for i, (x, y) in enumerate(zip(a, b)):
            yield from differences(x, y, path + f'[{i}]')
    elif isinstance(a, (float, int)) and isinstance(b, (float, int)) and not isinstance(a, bool) and not isinstance(b, bool):
        if not math.isclose(a, b, rel_tol=2e-4, abs_tol=2e-4):
            yield path, a, b
    elif a != b:
        yield path, a, b


if __name__ == '__main__':
    reference, actual = map(load, sys.argv[1:3])
    failed = 0
    for key, expected in reference.items():
        diff = list(differences(expected, actual.get(key, 'MISSING')))
        if diff:
            failed += 1
            if '-q' not in sys.argv:
                print(key, '; '.join(f'{p}: Unity {a}, Unbound {b}' for p, a, b in diff[:4]))
    print(f'constraints: {len(reference) - failed}/{len(reference)} records match, {failed} differ')
    sys.exit(int(failed > 0 or not reference))
