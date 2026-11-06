#!/usr/bin/env python3
"""Compare two stub-default recordings.

Every value is compared exactly as the probe wrote it (G9 floats), except the members listed in
COMPUTED: their comma-separated floats may differ by REL_TOL relative (with ABS_TOL around zero),
the float rounding of a computation done in a different order. Every differing key is printed.
"""
import json
import math
import sys

COMPUTED = {'Camera.CalculateObliqueMatrix'}
REL_TOL = 1e-6
ABS_TOL = 1e-6


def read(path):
    result = {}
    for line in open(path):
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError('duplicate key: ' + row['key'])
        result[row['key']] = row['value']
    if result.get('complete') != '1':
        raise ValueError('incomplete recording: ' + str(path))
    return result


def close(a, b):
    try:
        xs, ys = [float(v) for v in a.split(',')], [float(v) for v in b.split(',')]
    except ValueError:
        return False
    return len(xs) == len(ys) and all(math.isclose(x, y, rel_tol=REL_TOL, abs_tol=ABS_TOL) for x, y in zip(xs, ys))


def same(key, a, b):
    if a == b:
        return True
    return key in COMPUTED and a is not None and b is not None and close(a, b)


def main():
    a, b = map(read, sys.argv[1:3])
    keys = sorted(a.keys() | b.keys())
    bad = [key for key in keys if not same(key, a.get(key), b.get(key))]
    for key in bad:
        print(f'{key}: reference {a.get(key)}, candidate {b.get(key)}')
    within = [key for key in keys if key not in bad and a.get(key) != b.get(key)]
    for key in within:
        print(f'{key}: within tolerance: reference {a.get(key)}, candidate {b.get(key)}')
    print(f'{len(keys) - len(bad)}/{len(keys)} records match ({len(within)} within tolerance); {len(bad)} differ')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
