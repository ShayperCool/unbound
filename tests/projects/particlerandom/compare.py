#!/usr/bin/env python3
"""Compare two particle random recordings: every key on both sides, the same number of values,
and each value within 2e-4 absolute plus 2e-4 relative (counts, seeds and color bytes therefore
exactly). Seed halves and counts compare exactly.

A KNOWN rule names records (by regex) that differ for a recorded cause. The values before its
index still compare as above; the rest is printed as a known difference and does not fail.
Every difference is listed with both full values.
"""
import json
import re
import sys

TOLERANCE = 2e-4
KNOWN = [
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


def close(a, b, key=""):
    if a is None or b is None or len(a) != len(b):
        return False
    if key.startswith("burst/"):
        return a == b
    if key:
        if a[0] != b[0]:
            return False
        stride = 3 if key.startswith("mesh/") else 33
        if "/custom" not in key and key != "complete":
            for offset in range(1, len(a), stride):
                if a[offset:offset + 2] != b[offset:offset + 2]:
                    return False
                if stride == 3 and a[offset + 2] != b[offset + 2]:
                    return False
    return a is not None and b is not None and len(a) == len(b) and all(
        abs(x - y) <= TOLERANCE + TOLERANCE * abs(x) for x, y in zip(a, b))


def known_rule(key):
    return next((rule for rule in KNOWN if re.search(rule[0], key)), None)


def main():
    a, b = map(read, sys.argv[1:3])
    keys = sorted(a.keys() | b.keys())
    differ = [key for key in keys if not close(a.get(key), b.get(key), key)]
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
        group = key.split('/')[0] + '/' + key.split('/')[1] if '/' in key else key
        groups[group] = groups.get(group, 0) + 1
    print(f'{len(keys) - len(differ)}/{len(keys)} records match; {len(known)} known difference(s); '
          f'{len(bad)} differ {groups}')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
