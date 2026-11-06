#!/usr/bin/env python3
"""Compares a main-thread probe capture with the Unity reference.

    compare.py REFERENCE CAPTURE

Every (thread kind, call) of either file is listed when it is missing, extra or has another
outcome; the outcome texts (exception type and full message) are compared exactly. Exits 1 on
any difference or a capture without its completion marker.
"""
import json
import sys


def load(path):
    rows, complete = {}, False
    with open(path) as handle:
        for line in handle:
            if not line.strip():
                continue
            row = json.loads(line)
            if row.get('complete'):
                complete = True
                continue
            rows[(row['thread'], row['call'])] = row['result']
    return rows, complete


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    reference, _ = load(sys.argv[1])
    capture, complete = load(sys.argv[2])
    failures = 0
    for key in reference:
        if key not in capture:
            print(f'missing {key[0]} {key[1]}: expected {reference[key]!r}')
            failures += 1
        elif capture[key] != reference[key]:
            print(f'differs {key[0]} {key[1]}:\n  unity   {reference[key]!r}\n  unbound {capture[key]!r}')
            failures += 1
    for key in capture:
        if key not in reference:
            print(f'extra {key[0]} {key[1]}: {capture[key]!r}')
            failures += 1
    if not complete:
        print('capture has no completion marker')
        failures += 1
    matching = sum(1 for key in reference if capture.get(key) == reference[key])
    print(f'{matching}/{len(reference)} match, {failures} differences')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
