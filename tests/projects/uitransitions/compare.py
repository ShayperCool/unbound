#!/usr/bin/env python3
"""Compare deterministic transition samples. Values tolerate only float/layout rounding."""
import json
import math
import sys
from itertools import zip_longest
from pathlib import Path


def compare(reference, candidate):
    a = [json.loads(line) for line in Path(reference).read_text().splitlines()]
    b = [json.loads(line) for line in Path(candidate).read_text().splitlines()]
    errors = []
    if len(a) != len(b):
        errors.append(f"sample count {len(a)} != {len(b)}")
    for i, (x, y) in enumerate(zip_longest(a, b)):
        if x is None or y is None:
            errors.append(f"sample[{i}]: {x} != {y}")
            continue
        key = f"{i}: {x['scenario']} at {x['ms']}ms"
        for field in ('scenario', 'ms', 'events'):
            if x[field] != y[field]:
                errors.append(f"{key} {field}: {x[field]} != {y[field]}")
        if len(x['values']) != len(y['values']):
            errors.append(f"{key}: value count differs")
        for j, (u, v) in enumerate(zip_longest(x['values'], y['values'])):
            # Yoga rounds width/margins to physical pixels. Both engines use the same grid.
            if u is None or v is None or not math.isclose(u, v, rel_tol=2e-6, abs_tol=2e-5):
                errors.append(f"{key} value[{j}]: {u} != {v}")
    for error in errors:
        print(error)
    print(f"{len(a)} samples, {sum(len(r['events']) for r in a)} callbacks, {len(errors)} differences")
    return not errors


if __name__ == '__main__':
    sys.exit(0 if compare(*sys.argv[1:]) else 1)
