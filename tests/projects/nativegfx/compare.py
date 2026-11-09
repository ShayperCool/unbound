#!/usr/bin/env python3
"""Compare a native rendering plugin probe capture with the Editor's reference.

    compare.py REFERENCE.jsonl ACTUAL.jsonl

Every value compares exactly over the union of keys (pixels as runs, plugin records, pointer
relations, log messages); Mono's argument exception messages are compared in
CoreCLR's wording. Every missing, extra and failing value is printed; the exit status is nonzero
when anything differs, except for the keys `residuals.json` (next to this file) lists with their
cause and follow-up: those are printed with their reason, and printed as resolved once they match.
"""
import json
import re
import sys
from pathlib import Path

RESIDUALS = Path(__file__).resolve().parent/'residuals.json'


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


def residuals():
    """{key: reason} of the known residuals."""
    if not RESIDUALS.exists():
        return {}
    return {key: reason for reason, keys in json.loads(RESIDUALS.read_text()).items() for key in keys}


def main():
    ref, act = rows(sys.argv[1]), rows(sys.argv[2])
    known = residuals()
    bad = residual = 0
    for key in sorted(set(ref) | set(act)):
        if key not in act:
            kind, record = 'missing', {'key': key, 'reference': ref[key]}
        elif key not in ref:
            kind, record = 'extra', {'key': key, 'actual': act[key]}
        elif ref[key] != act[key]:
            kind, record = 'failing', {'key': key, 'reference': ref[key], 'actual': act[key]}
        else:
            if key in known:
                print('residual resolved (remove it from residuals.json):', key)
            continue
        if key in known:
            print('residual', kind, json.dumps({**record, 'reason': known[key]}))
            residual += 1
        else:
            print(kind, json.dumps(record))
            bad += 1
    total = len(set(ref) | set(act))
    print(f'{total - bad - residual}/{total} records match, {residual} known residuals')
    return int(bad > 0)


if __name__ == '__main__':
    sys.exit(main())
