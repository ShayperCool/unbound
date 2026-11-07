#!/usr/bin/env python3
"""Compare every key and value of a particletrails capture with the reference.

Values agree within 0.0002 absolute plus 0.0002 relative (the particle probes' bound). A missing
or extra key, a different length or any differing value fails; every failure is printed with the
first differing positions and both values there.

`--known FILE` names differences outside the probe's subject, by exact key and class with its reason
(particletrails' known.json). A listed key that differs is printed as KNOWN, with the same detail,
and does not fail; a listed key that matches counts as passing and is printed as NOW MATCHES so the
list can shrink. Prints one summary line:
`<passing>/<total> records match; <known> known difference(s); <failing> failing`.
"""
import argparse
import json
import sys

TOLERANCE = 2e-4
SHOWN = 6


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


def difference(r, c):
    """Why record c differs from r, or None when it matches."""
    if r is None or c is None:
        return 'extra' if r is None else 'missing'
    if len(r) != len(c):
        return 'length %d != %d (reference %s, candidate %s)' % (len(r), len(c), r[:SHOWN], c[:SHOWN])
    bad = [i for i, (x, y) in enumerate(zip(r, c)) if not near(x, y)]
    if bad:
        return '%d values differ: ' % len(bad) + ', '.join('[%d] %r != %r' % (i, r[i], c[i]) for i in bad[:SHOWN])
    return None


def read_known(path):
    """{key: class} from a known-difference file {class: {"reason", "keys"}}."""
    known = {}
    for name, entry in json.load(open(path)).items():
        if not entry.get('reason'):
            raise ValueError('known class without a reason: ' + name)
        for key in entry['keys']:
            if key in known:
                raise ValueError('key listed twice: ' + key)
            known[key] = name
    return known


def compare(reference, candidate, known=None):
    """Returns (passing keys, known differences, failures, known keys now matching); a difference
    is (key, reason) and a known one (key, class, reason)."""
    known = known or {}
    failures, listed, now = [], [], []
    passing = 0
    for key in sorted(set(reference) | set(candidate)):
        why = difference(reference.get(key), candidate.get(key))
        if why is None:
            passing += 1
            if key in known:
                now.append(key)
        elif key in known:
            listed.append((key, known[key], why))
        else:
            failures.append((key, why))
    return passing, listed, failures, now


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('candidate')
    parser.add_argument('--known', help='known differences, by class and exact key')
    args = parser.parse_args()
    reference, candidate = read(args.reference), read(args.candidate)
    known = read_known(args.known) if args.known else {}
    passing, listed, failures, now = compare(reference, candidate, known)
    for key, name, why in listed:
        print('KNOWN', name, key, why)
    for key in now:
        print('NOW MATCHES', known[key], key)
    for key, why in failures:
        print('FAIL', key, why)
    total = len(set(reference) | set(candidate))
    print('%d/%d records match; %d known difference(s); %d failing' % (passing, total, len(listed), len(failures)))
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
