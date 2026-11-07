#!/usr/bin/env python3
"""Compare every key and value of a particlecurves capture with the reference.

Values agree within 0.0002 absolute plus 0.0002 relative (the particle probes' bound); mode,
count and sentinel values are compared by the same rule. A missing or extra key, a different
length or any differing value fails, and every failure is printed with both raw records.
"""
import json
import sys

TOLERANCE = 2e-4


def read(path):
    result = {}
    for line in open(path):
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError('duplicate key: ' + row['key'])
        result[row['key']] = row['value']
    if result.get('complete') != [1]:
        raise ValueError('incomplete recording: ' + str(path))
    return result


def near(x, y):
    return abs(x - y) <= TOLERANCE + TOLERANCE * abs(x)


def compare(reference, candidate):
    """Returns (passing keys, failures) where a failure is (key, reason, reference, candidate)."""
    failures = []
    passing = 0
    for key in sorted(set(reference) | set(candidate)):
        r, c = reference.get(key), candidate.get(key)
        if r is None or c is None:
            failures.append((key, 'extra' if r is None else 'missing', r, c))
        elif len(r) != len(c):
            failures.append((key, 'length %d != %d' % (len(r), len(c)), r, c))
        else:
            bad = [i for i, (x, y) in enumerate(zip(r, c)) if not near(x, y)]
            if bad:
                failures.append((key, 'values at ' + ','.join(map(str, bad)), r, c))
            else:
                passing += 1
    return passing, failures


def main():
    if len(sys.argv) != 3:
        raise SystemExit('usage: compare.py REFERENCE CANDIDATE')
    reference, candidate = read(sys.argv[1]), read(sys.argv[2])
    passing, failures = compare(reference, candidate)
    for key, reason, r, c in failures:
        print('FAIL', key, reason)
        print('  reference', r)
        print('  candidate', c)
    total = len(set(reference) | set(candidate))
    print('%d/%d records match; %d failing' % (passing, total, len(failures)))
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
