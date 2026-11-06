#!/usr/bin/env python3
"""Exact comparison of a textureformats capture with the Unity reference.

    compare.py REFERENCE.jsonl CANDIDATE.jsonl [--report REPORT.json] [--only P1,P2]
               [--residuals residuals.json]

Values are float bits, raw bytes and API answers; every one must be equal (no tolerance: a
numeric one would hide 16-bit to 8-bit loss). Every missing, extra and failing key is printed
(and written to the report). --only compares the keys of the cases a TEXTURE_FORMATS_PROBE_ONLY
run recorded (keys starting with one of the prefixes plus a dot, and `complete`).
--residuals names recorded differences ({key: why}): they are still printed and reported, and
each must still differ (a listed key that matches is an error, so the list cannot go stale).
Exit status 1 when anything is missing, extra or failing outside the residuals.
"""
import argparse
import json
from pathlib import Path


def load(path):
    rows = {}
    for line in Path(path).read_text().splitlines():
        if not line.strip():
            continue
        row = json.loads(line)
        if row['k'] in rows:
            raise ValueError('duplicate key: '+row['k'])
        rows[row['k']] = row['v']
    return rows


def selected(rows, only):
    if not only:
        return rows
    prefixes = [p+'.' for p in only.split(',')]
    return {k: v for k, v in rows.items() if k == 'complete' or any(k.startswith(p) for p in prefixes)}


def compare(reference, candidate, only=None):
    a = selected(load(reference), only)
    b = selected(load(candidate), only) if Path(candidate).exists() else {}
    return {'reference': str(reference), 'candidate': str(candidate),
            'reference_count': len(a), 'candidate_count': len(b),
            'missing': [{'key': k, 'expected': a[k]} for k in sorted(a.keys()-b.keys())],
            'extra': [{'key': k, 'actual': b[k]} for k in sorted(b.keys()-a.keys())],
            'failing': [{'key': k, 'expected': a[k], 'actual': b[k]}
                        for k in sorted(a.keys() & b.keys()) if a[k] != b[k]]}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference', type=Path)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--report', type=Path)
    parser.add_argument('--only', help='comma-separated case prefixes (rt.ARGB64, tex.RG32, ...)')
    parser.add_argument('--residuals', type=Path, help='JSON {key: why} of recorded differences')
    args = parser.parse_args()
    result = compare(args.reference, args.candidate, args.only)
    residuals = json.loads(args.residuals.read_text()) if args.residuals else {}
    for kind, label in (('missing', 'expected'), ('extra', 'actual')):
        for row in result[kind]:
            print(f"{kind} {row['key']}: {json.dumps(row[label])}")
    for row in result['failing']:
        print(f"failing {row['key']}: unity {json.dumps(row['expected'])} unbound {json.dumps(row['actual'])}")
    counts = {k: len(result[k]) for k in ('missing', 'extra', 'failing')}
    differing = {row['key'] for kind in ('missing', 'extra', 'failing') for row in result[kind]}
    in_selection = set(selected({k: None for k in residuals}, args.only))
    result['residuals'] = sorted(differing & in_selection)
    result['stale_residuals'] = sorted(in_selection - differing)
    unexplained = sorted(differing - in_selection)
    for key in result['stale_residuals']:
        print(f'stale residual {key}: it matches the reference now; remove it from the residuals')
    print(json.dumps({'reference_count': result['reference_count'], **counts,
                      'residuals': len(result['residuals']), 'stale_residuals': len(result['stale_residuals']),
                      'unexplained': len(unexplained)}))
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(result, indent=2)+'\n')
    return int(bool(unexplained or result['stale_residuals']))


if __name__ == '__main__':
    raise SystemExit(main())
