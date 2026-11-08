#!/usr/bin/env python3
"""Compares a main-thread probe capture with the Unity reference.

    compare.py REFERENCE CAPTURE

Every (thread kind, call) of either file is listed when it is missing, extra or has another
outcome; the outcome texts (exception type and full message) are compared exactly. Reports every difference. Only the exact return-value pairs in residuals.json are known
subsystem follow-ups; all other differences and missing completion markers exit 1.
"""
import json
import sys
from pathlib import Path


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
    residuals = {(r['thread'], r['call']): r for r in json.loads(
        Path(__file__).with_name('residuals.json').read_text())}
    failures, known = 0, 0
    for key in reference:
        if key not in capture:
            print(f'missing {key[0]} {key[1]}: expected {reference[key]!r}')
            failures += 1
        elif capture[key] != reference[key]:
            print(f'differs {key[0]} {key[1]}:\n  unity   {reference[key]!r}\n  unbound {capture[key]!r}')
            residual = residuals.get(key)
            if residual and reference[key] == residual['unity'] and capture[key] == residual['unbound']:
                print('  known residual:', residual['reason'])
                known += 1
            else:
                failures += 1
    for key in capture:
        if key not in reference:
            print(f'extra {key[0]} {key[1]}: {capture[key]!r}')
            failures += 1
    if not complete:
        print('capture has no completion marker')
        failures += 1
    matching = sum(1 for key in reference if capture.get(key) == reference[key])
    print(f'{matching}/{len(reference)} match, {failures} failures, {known} known residuals')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
