#!/usr/bin/env python3
"""Compare every defined canvas color using the particle-stream 2e-4 tolerance."""
import json
import math
import sys
from pathlib import Path


def load(path):
    records = [json.loads(line) for line in (Path(path) / 'values.jsonl').read_text().splitlines()]
    values = {record['k']: record['v'] for record in records}
    assert len(values) == len(records), 'duplicate keys'
    return values


def equal(a, b):
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(equal(x, y) for x, y in zip(a, b))
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return math.isfinite(a) and math.isfinite(b) and math.isclose(a, b, rel_tol=2e-4, abs_tol=2e-4)
    return a == b


if __name__ == '__main__':
    reference, actual = map(load, sys.argv[1:3])
    failures = [k for k in reference if not equal(reference[k], actual.get(k, 'MISSING'))]
    if '-q' not in sys.argv:
        for key in failures:
            print(f'{key}: Unity {reference[key]}, candidate {actual.get(key, "MISSING")}')
    extras = actual.keys() - reference.keys()
    print(f'canvas colors: {len(reference)-len(failures)}/{len(reference)} records match, {len(failures)} differ, {len(extras)} unexpected')
    sys.exit(int(bool(failures or extras or not reference)))
