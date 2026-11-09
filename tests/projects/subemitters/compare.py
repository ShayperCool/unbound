#!/usr/bin/env python3
"""Compare two sub-emitter recordings: every key on both sides, the same number of values, and
each value within 2e-4 absolute plus 2e-4 relative (counts, colors and settings therefore exactly).

`random/` records are [parent count, child count], both compared exactly within the same numeric
bound as the other records. Seeded probability and count-range cases no longer use a count range.

KNOWN rules name records (by regex) with fields that differ for a cause this probe does not fix;
the other values of those records (always the count) still compare as above, the known fields are
printed with their values and do not fail. Every difference is listed. Particle records are the
count, then 18 values per particle: position 0-2, velocity 3-5, remaining lifetime 6, start lifetime
7, start size 8-10, start color 11-14, rotation 15-17.
"""
import json
import re
import sys

TOLERANCE = 2e-4
FIELDS = 18
POSITION, VELOCITY = {0, 1, 2}, {3, 4, 5}
# Rules (regex, fields, reason) for records that differ for a cause this probe does not fix; none
# at present.
KNOWN = []


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


def close(a, b, skip=frozenset()):
    return a is not None and b is not None and len(a) == len(b) and all(
        i in skip or near(x, y) for i, (x, y) in enumerate(zip(a, b)))


def known(key, a, b):
    """The reason and the value indices a KNOWN rule leaves out of the comparison, if any applies."""
    if a is None or b is None:
        return None
    for pattern, fields, reason in KNOWN:
        if re.search(pattern, key) and len(a) == len(b) and len(a) == 1 + int(a[0]) * FIELDS:
            return reason, {1 + i * FIELDS + f for i in range(int(a[0])) for f in fields}
    return None


def main():
    a, b = map(read, sys.argv[1:3])
    keys = sorted(a.keys() | b.keys())
    known_keys, bad = [], []
    for key in keys:
        ra, rb = a.get(key), b.get(key)
        ok = close(ra, rb)
        if not ok:
            rule = known(key, ra, rb)
            if rule and close(ra, rb, rule[1]):
                known_keys.append(key)
                print(f'{key}: known difference ({rule[0]}): reference {ra}, candidate {rb}')
                continue
        if not ok:
            bad.append(key)
            print(f'{key}: reference {ra}, candidate {rb}')
    groups = {}
    for key in bad:
        group = key.rsplit('/', 1)[0]
        groups[group] = groups.get(group, 0) + 1
    print(f'{len(keys) - len(known_keys) - len(bad)}/{len(keys)} records match; {len(known_keys)} known difference(s); '
          f'{len(bad)} differ {groups}')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
