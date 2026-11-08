#!/usr/bin/env python3
"""Compare every key and value of a particlecurves capture with the reference.

Values agree within 0.0002 absolute plus 0.0002 relative (the particle probes' bound); mode,
count and sentinel values are compared by the same rule. A missing or extra key, a different
length or any differing value fails, and every failure is printed with both raw records.
`--known FILE` names differences by class and exact key (particletrails' known.json format): a
listed key that differs prints as KNOWN with both records and does not fail; one that matches
prints as NOW MATCHES.
"""
import argparse
import importlib.util
import json
import sys
from pathlib import Path


def read_known(path):
    """The known-difference reader of particletrails/compare.py (the particle probes' format)."""
    spec = importlib.util.spec_from_file_location('particletrails_compare', Path(__file__).resolve().parents[1]/'particletrails'/'compare.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.read_known(path)

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
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('candidate')
    parser.add_argument('--known', help='known differences, by class and exact key')
    args = parser.parse_args()
    reference, candidate = read(args.reference), read(args.candidate)
    known = read_known(args.known) if args.known else {}
    passing, failures = compare(reference, candidate)
    listed = [f for f in failures if f[0] in known]
    failures = [f for f in failures if f[0] not in known]
    failed = {f[0] for f in listed}
    for key in sorted(k for k in known if k not in failed and k in reference):
        print('NOW MATCHES', known[key], key)
    for key, reason, r, c in listed:
        print('KNOWN', known[key], key, reason)
        print('  reference', r)
        print('  candidate', c)
    for key, reason, r, c in failures:
        print('FAIL', key, reason)
        print('  reference', r)
        print('  candidate', c)
    total = len(set(reference) | set(candidate))
    print('%d/%d records match; %d known difference(s); %d failing' % (passing, total, len(listed), len(failures)))
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
