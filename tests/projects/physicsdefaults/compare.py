#!/usr/bin/env python3
"""Compare every record, retaining the physics fixture's 1e-3 relative motion tolerance."""
import json
import math
import sys


def read(path):
    result = {}
    for line in open(path):
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError('duplicate key: ' + row['key'])
        result[row['key']] = row['value']
    if not result:
        raise ValueError('empty recording: ' + path)
    return result


def main():
    a, b = map(read, sys.argv[1:3])
    bad = solver_bad = passed = 0
    for key in sorted(a.keys() | b.keys()):
        av, bv = a.get(key), b.get(key)
        tolerance = 1e-3 if key.endswith('/motion') else 1e-6
        match = av is not None and bv is not None and len(av) == len(bv)
        if match:
            match = all(x == y if isinstance(x, str) or isinstance(y, str) else
                        math.isfinite(x) and math.isfinite(y) and abs(x-y) <= tolerance*max(1,abs(x))
                        for x,y in zip(av,bv))
        if match:
            passed += 1
        else:
            bad += 1
            solver_bad += key.startswith('dynamics/solver/') and key.endswith('/motion')
            print(f'{key}: reference {av}, candidate {bv}')
    print(f'{passed}/{len(a.keys() | b.keys())} records match; {bad} differ; {solver_bad} solver motion differences')
    return bool(bad)


if __name__ == '__main__':
    sys.exit(main())
