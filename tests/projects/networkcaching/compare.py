#!/usr/bin/env python3
"""Compare every record of a capture with the reference, exactly.

Keys starting with "os." hold what the operating system or Unbound's own layout decides (the
default cache's path, the platform); they are listed with both values but never compared.
"""
import json
import pathlib
import sys


def read(path):
    records = [json.loads(line) for line in pathlib.Path(path).read_text().splitlines()]
    result = {r['key']: r['value'] for r in records}
    assert len(result) == len(records), 'duplicate keys'
    return result


expected, actual = map(read, sys.argv[1:3])
failed = compared = 0
for key in sorted(expected.keys() | actual.keys()):
    if key.startswith('os.'):
        print(f'{key} (not compared): Unity={expected.get(key)!r}, Unbound={actual.get(key)!r}')
        continue
    compared += 1
    if expected.get(key) != actual.get(key):
        print(f'{key}: Unity={expected.get(key)!r}, Unbound={actual.get(key)!r}')
        failed += 1
print(f'{compared - failed}/{compared} records match')
sys.exit(bool(failed))
