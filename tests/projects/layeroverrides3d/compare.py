#!/usr/bin/env python3
"""Compare every record of two layer-override recordings exactly (counts and flags only)."""
import json
import sys


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


def main():
    a, b = map(read, sys.argv[1:3])
    keys = sorted(a.keys() | b.keys())
    bad = [key for key in keys if a.get(key) != b.get(key)]
    for key in bad:
        print(f'{key}: reference {a.get(key)}, candidate {b.get(key)}')
    groups = {}
    for key in bad:
        groups[key.split('/')[0]] = groups.get(key.split('/')[0], 0) + 1
    print(f'{len(keys) - len(bad)}/{len(keys)} records match; {len(bad)} differ {groups}')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
