#!/usr/bin/env python3
"""Compares two sub-emitter first-age records over the full union of their leaf values, exactly.

    compare.py REFERENCE.jsonl ACTUAL(.jsonl|DIR) [--report REPORT.json]

Each record line {"k": key, "v": value} is flattened to leaves `key` + JSON pointer (`/particles/0/
life`); lists keep their order (nothing is sorted or filtered). Every leaf of either side is in the
report: `missing` (only in the reference), `extra` (only in the actual record) and `differ` (both
values kept). There is no tolerance and no ignored value: numbers are compared as the values the
probe printed (float32 round trips), so any difference fails. Exit 0 only when nothing fails.
"""
import argparse
import json
from pathlib import Path
import sys


def leaves(value, path=''):
    if isinstance(value, dict):
        for k, v in value.items():
            yield from leaves(v, f'{path}/{k}')
    elif isinstance(value, list):
        if not value:
            yield path, []
        for i, v in enumerate(value):
            yield from leaves(v, f'{path}/{i}')
    else:
        yield path, value


def load(path):
    path = Path(path)
    if path.is_dir():
        path = path/'values.jsonl'
    flat = {}
    for line in path.read_text().splitlines():
        row = json.loads(line)
        for pointer, value in leaves(row['v']):
            flat[row['k']+pointer] = value
    return flat


def number(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def same(a, b):
    """Exact: numbers by value (an integral float prints as an integer), everything else by type and value."""
    if number(a) and number(b):
        return a == b
    return type(a) is type(b) and a == b


def group(key):
    return key.split('.', 1)[0] if not key.startswith('case.') else '.'.join(key.split('.', 2)[:2])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    unity, unbound = load(args.reference), load(args.actual)
    missing = {k: unity[k] for k in unity if k not in unbound}
    extra = {k: unbound[k] for k in unbound if k not in unity}
    differ = {k: {'unity': unity[k], 'unbound': unbound[k]} for k in unity if k in unbound and not same(unity[k], unbound[k])}
    failing = sorted({*missing, *extra, *differ})
    groups = {}
    for k in failing:
        groups[group(k)] = groups.get(group(k), 0)+1
    report = {'reference': str(args.reference), 'actual': str(args.actual), 'union': len(set(unity) | set(unbound)),
              'passing': len(set(unity) | set(unbound))-len(failing), 'failing': len(failing), 'groups': groups,
              'missing': missing, 'extra': extra, 'differ': differ, 'tolerance': None}
    if args.report:
        args.report.write_text(json.dumps(report, indent=1)+'\n')
    print(f"{report['passing']} of {report['union']} leaf values match; {len(missing)} missing, {len(extra)} extra, "
          f"{len(differ)} differ")
    for g, n in sorted(groups.items()):
        print(f'  {g}: {n}')
    return 1 if failing else 0


if __name__ == '__main__':
    sys.exit(main())
