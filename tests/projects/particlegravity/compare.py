#!/usr/bin/env python3
"""Compare two particle gravity recordings: every key on both sides, the same number of values,
and each value within 2e-4 absolute plus 2e-4 relative (counts and settings therefore exactly).

A KNOWN rule names records (by regex) whose values from an index on differ for a cause outside
particle gravity. The values before that index (the count, the first particle) still compare as
above; the rest is printed as a known difference and does not fail. Every difference is listed.
"""
import json
import re
import sys

TOLERANCE = 2e-4
KNOWN = [
    (r'^random/', 1, 'per-particle random values are Unbound\'s own stream, not Unity\'s'),
    (r'^custom/rotated_scaled/\d+$', 1,
     'start velocity in a scaled custom space: Unity keeps the start speed along the '
     'inverse-transformed direction, Unbound scales it'),
    (r'/(23|24)$', 7, 'the loop-wrap burst particle: Unity ages it by the whole Simulate step'),
]


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


def close(a, b):
    return a is not None and b is not None and len(a) == len(b) and all(
        abs(x - y) <= TOLERANCE + TOLERANCE * abs(x) for x, y in zip(a, b))


def known_rule(key):
    return next((rule for rule in KNOWN if re.search(rule[0], key)), None)


def main():
    a, b = map(read, sys.argv[1:3])
    keys = sorted(a.keys() | b.keys())
    differ = [key for key in keys if not close(a.get(key), b.get(key))]
    known, bad = [], []
    for key in differ:
        rule = known_rule(key)
        ra, rb = a.get(key), b.get(key)
        if rule and ra is not None and rb is not None and close(ra[:rule[1]], rb[:rule[1]]):
            known.append(key)
            print(f'{key}: known difference ({rule[2]}): reference {ra}, candidate {rb}')
        else:
            bad.append(key)
            print(f'{key}: reference {ra}, candidate {rb}')
    groups = {}
    for key in bad:
        group = key.rsplit('/', 1)[0]
        groups[group] = groups.get(group, 0) + 1
    print(f'{len(keys) - len(differ)}/{len(keys)} records match; {len(known)} known difference(s); '
          f'{len(bad)} differ {groups}')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
