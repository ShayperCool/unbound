#!/usr/bin/env python3
"""Compare a command buffer probe capture with the Editor's reference.

    compare.py REFERENCE.jsonl ACTUAL.jsonl

Every value compares exactly over the union of keys (buffer words, readback bytes as hex,
descriptors, exceptions, log messages); Mono's argument exception messages are compared in
CoreCLR's wording. Every missing, extra and failing value is printed; the exit status is nonzero
when anything differs.
"""
import json
import re
import sys


def normalize(value):
    """Mono's "message\\nParameter name: x" as CoreCLR's "message (Parameter 'x')"."""
    if isinstance(value, str):
        return re.sub(r"\nParameter name: (.*)$", r" (Parameter '\1')", value)
    if isinstance(value, list):
        return [normalize(v) for v in value]
    return value


def rows(path):
    result = {}
    for line in open(path):
        if line.strip():
            row = json.loads(line)
            if row['k'] in result:
                raise SystemExit(f'{path}: duplicate key {row["k"]}')
            result[row['k']] = normalize(row['v'])
    return result


def main():
    ref, act = rows(sys.argv[1]), rows(sys.argv[2])
    bad = 0
    for key in ref:
        if key not in act:
            print('missing', json.dumps({'key': key, 'reference': ref[key]}))
            bad += 1
        elif ref[key] != act[key]:
            print('failing', json.dumps({'key': key, 'reference': ref[key], 'actual': act[key]}))
            bad += 1
    for key in act:
        if key not in ref:
            print('extra', json.dumps({'key': key, 'actual': act[key]}))
            bad += 1
    total = len(set(ref) | set(act))
    print(f'{total - bad}/{total} records match')
    return int(bad > 0)


if __name__ == '__main__':
    sys.exit(main())
