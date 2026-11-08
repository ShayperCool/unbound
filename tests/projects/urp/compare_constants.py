#!/usr/bin/env python3
"""Compares the per-object constants the URP probe dumps (per-object-constants.jsonl: what each
renderer of the baked scenes draws with, read back from a float target) with Unity's. Exits 1 on
any missing, extra or differing value; every one is listed. Also compares texture-formats.jsonl
(names compare exactly).

    python3 tests/projects/urp/compare_constants.py \\
        tests/projects/urp/Reference/scenarios/per-object-constants.jsonl /tmp/urp/per-object-constants.jsonl

Values agree within 2e-3 relative (Unbound's float targets hold half floats); infinities exactly.
"""
import json
import math
import sys

TOLERANCE = 2e-3


def load(path):
    values = {}
    for line in open(path):
        if line.strip():
            entry = json.loads(line)
            values[entry['key']] = entry['value']
    return values


def close(a, b):
    if math.isinf(a) or math.isinf(b) or math.isnan(a) or math.isnan(b):
        return a == b or (math.isnan(a) and math.isnan(b))
    return abs(a - b) <= TOLERANCE * max(1.0, abs(a))


def main():
    unity, unbound = load(sys.argv[1]), load(sys.argv[2])
    differences = 0
    for key, expected in unity.items():
        actual = unbound.get(key)
        if actual is None:
            print(f'missing  {key}')
            differences += 1
        elif isinstance(expected, str) or isinstance(actual, str):
            if actual != expected:
                print(f'differs  {key}: unity {expected} unbound {actual}')
                differences += 1
        elif len(actual) != len(expected) or not all(close(a, b) for a, b in zip(expected, actual)):
            print(f'differs  {key}: unity {expected} unbound {actual}')
            differences += 1
    for key in unbound.keys() - unity.keys():
        print(f'extra    {key}')
        differences += 1
    print(f'{len(unity) - differences}/{len(unity)} values match, differences: {differences}')
    return 1 if differences else 0


if __name__ == '__main__':
    sys.exit(main())
