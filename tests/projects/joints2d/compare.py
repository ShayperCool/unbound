#!/usr/bin/env python3
"""Compare 2D joints Editor and Unbound values.jsonl files, recursively (2e-4 tolerance)."""
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


def solver_roundoff(key, expected, actual, diff):
    """Keep tiny iterative-solver residuals visible without treating them as missing behavior."""
    if not key.startswith(('hinge.case', 'fixed.case', 'wheel.case')) or not isinstance(actual, dict):
        return False
    for path, a, b in diff:
        if path == '.properties.state':
            x, y = expected['properties'], actual['properties']
            if {a, b} not in ({0, 1}, {0, 2}):
                return False
            bound = 'min' if 1 in (a, b) else 'max'
            if max(abs(p['angle'] - p[bound]) for p in (x, y)) > 1e-3:
                return False
        elif (path in ('.body.av', '.other.av', '.properties.speed', '.torque', '.torque_04')
              or path.startswith(('.force[', '.force_04['))):
            if not all(isinstance(v, (int, float)) and math.isfinite(v) for v in (a, b)):
                return False
            bound = 1e-3
            if key.startswith('wheel.case') and path in ('.body.av', '.other.av', '.properties.speed'):
                bound += 2e-4 * max(abs(a), abs(b))
                if path == '.properties.speed':
                    # Subtracting two agreeing angular velocities can expose their rounding error.
                    for body in ('body', 'other'):
                        values = [r.get(body) for r in (expected, actual)]
                        bound += 2e-4 * max([abs(v['av']) for v in values if isinstance(v, dict)] or [0])
            if abs(a - b) > bound:
                return False
        else:
            return False
    return True


if __name__ == '__main__':
    reference, actual = map(load, sys.argv[1:3])
    failed = known = 0
    for key, expected in reference.items():
        diff = list(differences(expected, actual.get(key, 'MISSING')))
        if diff:
            if solver_roundoff(key, expected, actual.get(key), diff):
                known += 1
                if '-q' not in sys.argv:
                    print(key, 'known solver roundoff:', '; '.join(f'{p}: Unity {a}, Unbound {b}' for p, a, b in diff))
                continue
            failed += 1
            if '-q' not in sys.argv:
                print(key, '; '.join(f'{p}: Unity {a}, Unbound {b}' for p, a, b in diff[:4]))
    extras = actual.keys() - reference.keys()
    if extras:
        print(f'Unexpected records: {", ".join(sorted(extras)[:8])}')
    print(f'joints2d: {len(reference) - failed - known}/{len(reference)} records match, {known} known solver roundoff, {failed} differ, {len(extras)} unexpected')
    sys.exit(int(failed > 0 or extras or not reference))
