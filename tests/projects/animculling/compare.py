#!/usr/bin/env python3
"""Compare two animation-culling recordings: compare.py REFERENCE CANDIDATE.

Every record of either side is compared: numbers within 1e-4 (NaN only matches NaN),
everything else exactly. Every
missing, extra and differing record is listed, then '<n>/<total> records match' and the
differing records by case."""
import json
import math
import sys

TOLERANCE = 1e-4


def read(path):
    result = {}
    for line in open(path):
        if not line.strip():
            continue
        row = json.loads(line)
        if row['k'] in result:
            raise ValueError('duplicate key: ' + row['k'])
        result[row['k']] = row['v']
    return result


def same(a, b):
    if isinstance(a, bool) or isinstance(b, bool):
        return a is b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        # A NaN the probe recorded (a NaN input weight read back) only matches NaN.
        if math.isnan(a) or math.isnan(b):
            return math.isnan(a) and math.isnan(b)
        return abs(a - b) <= TOLERANCE
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    return a == b


def case(key):
    return key.rsplit('/', 1)[0] if key.rsplit('/', 1)[-1].isdigit() else key


def main():
    reference, candidate = read(sys.argv[1]), read(sys.argv[2])
    keys = list(reference) + [k for k in candidate if k not in reference]
    bad = []
    for key in keys:
        if key not in candidate:
            print(f'missing {key}: reference {json.dumps(reference[key])}')
        elif key not in reference:
            print(f'extra {key}: candidate {json.dumps(candidate[key])}')
        elif not same(reference[key], candidate[key]):
            print(f'{key}: reference {json.dumps(reference[key])}, candidate {json.dumps(candidate[key])}')
        else:
            continue
        bad.append(key)
    groups = {}
    for key in bad:
        groups[case(key)] = groups.get(case(key), 0) + 1
    print(f'{len(keys) - len(bad)}/{len(keys)} records match; {len(bad)} differ {groups}')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
