#!/usr/bin/env python3
"""Compare a corecaps capture with the Editor's reference.

    compare.py REFERENCE.jsonl ACTUAL.jsonl --device opengl|vulkan [--name NAME]

Every value compares exactly over the union of keys. Missing, extra, and differing keys are
printed in full. Known Editor differences listed in residuals-<device>.json are printed with
their reason and do not fail the gate; resolved residuals are flagged.
"""
import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load(path):
    rows = {}
    with open(path, encoding='utf-8') as f:
        for line in f:
            if line.strip():
                row = json.loads(line)
                if row['k'] in rows:
                    raise SystemExit(f'{path}: duplicate key {row["k"]}')
                rows[row['k']] = row['v']
    if not rows:
        raise SystemExit(f'{path}: empty')
    return rows


def load_residuals(device):
    path = HERE / f'residuals-{device}.json'
    if not path.exists():
        return {}
    return {k: reason for reason, keys in json.loads(path.read_text(encoding='utf-8')).items() for k in keys}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('reference')
    p.add_argument('actual')
    p.add_argument('--device', choices=('opengl', 'vulkan'), default='opengl')
    p.add_argument('--name', default=None)
    p.add_argument('--no-residuals', action='store_true')
    args = p.parse_args()
    name = args.name or f'corecaps-{args.device}'
    ref, act = load(args.reference), load(args.actual)
    known = {} if args.no_residuals else load_residuals(args.device)
    bad = residual = 0
    for key in sorted(set(ref) | set(act)):
        if key not in act:
            kind, rec = 'missing', {'key': key, 'reference': ref[key]}
        elif key not in ref:
            kind, rec = 'extra', {'key': key, 'actual': act[key]}
        elif ref[key] != act[key]:
            kind, rec = 'failing', {'key': key, 'reference': ref[key], 'actual': act[key]}
        else:
            if key in known:
                print('residual resolved (remove from residuals):', key)
            continue
        if key in known:
            print('residual', kind, json.dumps({**rec, 'reason': known[key]}))
            residual += 1
        else:
            print(kind, json.dumps(rec))
            bad += 1
    total = len(set(ref) | set(act))
    print(f'{name}: {total - bad - residual}/{total} records match, {residual} known residuals')
    return int(bad > 0)


if __name__ == '__main__':
    sys.exit(main())
