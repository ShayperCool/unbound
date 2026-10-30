#!/usr/bin/env python3
"""Compare Effector2D Editor and Unbound values.jsonl files, recursively (2e-4 tolerance)."""
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


def contact_order_only(expected, actual, diff):
    if not isinstance(expected, dict) or not isinstance(actual, dict):
        return False
    if not diff or any(not path.startswith('.normals') for path, _, _ in diff):
        return False
    a, b = expected.get('normals'), actual.get('normals')
    if not isinstance(a, list) or not isinstance(b, list) or len(a) != len(b):
        return False
    unmatched = list(b)
    for normal in a:
        match = next((i for i, candidate in enumerate(unmatched) if not list(differences(normal, candidate))), None)
        if match is None:
            return False
        unmatched.pop(match)
    return not unmatched


def native_outcome(expected, actual):
    diff = list(differences(expected, actual))
    return not diff or contact_order_only(expected, actual, diff)


if __name__ == '__main__':
    reference, actual = map(load, sys.argv[1:3])
    variants_path = Path(sys.argv[1]) / 'initial-overlap-variants.json'
    variants = json.loads(variants_path.read_text())['records'] if variants_path.exists() else {}
    failed = known = 0
    for key, expected in reference.items():
        diff = list(differences(expected, actual.get(key, 'MISSING')))
        if diff:
            found = actual.get(key, 'MISSING')
            if contact_order_only(expected, found, diff):
                known += 1
                if '-q' not in sys.argv:
                    print(key, 'known native contact order')
                continue
            if any(native_outcome(alternate, found) for alternate in variants.get(key, [])):
                known += 1
                if '-q' not in sys.argv:
                    print(key, 'matches another measured native initial-overlap outcome')
                continue
            failed += 1
            if '-q' not in sys.argv:
                print(key, '; '.join(f'{p}: Unity {a}, Unbound {b}' for p, a, b in diff[:4]))
    extras = actual.keys() - reference.keys()
    if extras:
        print(f'Unexpected records: {", ".join(sorted(extras)[:8])}')
    print(f'effectors: {len(reference) - failed - known}/{len(reference)} records match, {known} known native contact-order records, {failed} differ, {len(extras)} unexpected')
    sys.exit(int(failed > 0 or extras or not reference))
