#!/usr/bin/env python3
"""Compare two device facade recordings exactly; every missing, extra or differing key is printed."""
import json
import sys


def read(path):
    result = {}
    for line in open(path):
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError('duplicate key: ' + row['key'])
        result[row['key']] = row['value']
    if result.get('complete') != '1':
        raise ValueError('incomplete recording: ' + str(path))
    return result


def main():
    a, b = map(read, sys.argv[1:3])
    keys = sorted(a.keys() | b.keys())
    bad = [key for key in keys if a.get(key) != b.get(key)]
    for key in bad:
        print(f'{key}: reference {a.get(key)}, candidate {b.get(key)}')
    print(f'{len(keys) - len(bad)}/{len(keys)} records match; {len(bad)} differ')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
