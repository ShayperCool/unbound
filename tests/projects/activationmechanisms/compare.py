#!/usr/bin/env python3
"""Compare the activation mechanism probe: Unity's reference against another capture.

    compare.py REFERENCE.jsonl CAPTURE_DIR_OR_values.jsonl [--report REPORT.json]

Every key of the union is compared exactly; there is no tolerance (the values are labels,
callback lists in raw order, flags, counts, sibling indices, relative frames, logs and exception
texts). A callback list in another order is a differing value. Each missing, extra and differing
key is listed with both values, and the report keeps them all. Exception keys (`key.exception`)
and log keys (`block.log`) are ordinary keys of the union. The record order of the common keys is
compared too: every common key whose position among the common keys differs is listed. Groups
summarize the differences (context, boot, setup, settle, op.<name>, contact.<name>,
particles.<name>, capture). Raw callback entries keep their nested built-in state; nothing is
normalized, sorted or rounded.
"""
import argparse
import json
from collections import Counter
from pathlib import Path


def load(path):
    path = Path(path)
    if path.is_dir():
        path = path/'values.jsonl'
    values, order = {}, []
    for number, line in enumerate(path.read_text().splitlines(), 1):
        if not line.strip():
            continue
        row = json.loads(line)
        if row['k'] in values:
            raise SystemExit(f'{path}:{number}: duplicate key {row["k"]}')
        values[row['k']] = row['v']
        order.append(row['k'])
    return values, order


def group(key):
    parts = key.split('.')
    if parts[0] in ('op', 'contact', 'particles') and len(parts) > 1:
        return f'{parts[0]}.{parts[1]}'
    return parts[0]


def compare(want, got):
    missing = sorted(k for k in want if k not in got)
    extra = sorted(k for k in got if k not in want)
    differ = sorted(k for k in want if k in got and got[k] != want[k])
    return missing, extra, differ


def reordered(want_order, got_order):
    common = set(want_order) & set(got_order)
    a = [k for k in want_order if k in common]
    b = [k for k in got_order if k in common]
    position = {k: i for i, k in enumerate(b)}
    return [{'key': k, 'unity': i, 'other': position[k]} for i, k in enumerate(a) if position[k] != i]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--report')
    args = parser.parse_args()
    (want, want_order), (got, got_order) = load(args.reference), load(args.capture)
    missing, extra, differ = compare(want, got)
    moved = reordered(want_order, got_order)
    for k in missing:
        print(f'missing {k}\n     unity   {json.dumps(want[k])}')
    for k in extra:
        print(f'extra   {k}\n     other   {json.dumps(got[k])}')
    for k in differ:
        print(f'differ  {k}\n     unity   {json.dumps(want[k])}\n     other   {json.dumps(got[k])}')
    for m in moved:
        print(f'order   {m["key"]}: position {m["unity"]} -> {m["other"]}')
    groups = Counter(group(k) for k in missing+extra+differ)
    for name, count in sorted(groups.items()):
        print(f'  {name}: {count}')
    total = len(set(want) | set(got))
    failing = len(missing)+len(extra)+len(differ)
    print(f'{failing} of {total} keys fail ({len(missing)} missing, {len(extra)} extra, {len(differ)} differ), '
          f'{len(moved)} common keys out of order')
    if args.report:
        Path(args.report).write_text(json.dumps({
            'reference': str(args.reference), 'capture': str(args.capture), 'keys': total, 'failing': failing,
            'passing': total-failing, 'tolerance': 'exact',
            'missing': {k: want[k] for k in missing}, 'extra': {k: got[k] for k in extra},
            'differ': {k: {'unity': want[k], 'other': got[k]} for k in differ},
            'order': moved, 'groups': dict(sorted(groups.items())),
        }, indent=1)+'\n')
    return 1 if failing or moved else 0


if __name__ == '__main__':
    raise SystemExit(main())
