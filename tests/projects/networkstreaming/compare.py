#!/usr/bin/env python3
"""Compare every stable observation. No chunk counts or frame timing are normalized away."""
import json
import pathlib
import sys


def read(path):
    records = [json.loads(line) for line in pathlib.Path(path).read_text().splitlines()]
    result = {r['key']: r['value'] for r in records}
    assert len(result) == len(records), 'duplicate keys'
    return result

expected, actual = map(read, sys.argv[1:])
failed = 0
for key in sorted(expected.keys() | actual.keys()):
    if expected.get(key) != actual.get(key):
        print(f'{key}: Unity={expected.get(key)!r}, Unbound={actual.get(key)!r}')
        failed += 1
print(f'{len(expected) - failed}/{len(expected)} observations match')
sys.exit(bool(failed))
