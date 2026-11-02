#!/usr/bin/env python3
"""Compare the shader pass count probe: Unity's reference against an Unbound capture.

    compare.py REFERENCE.jsonl CAPTURE_DIR_OR_values.jsonl [--report REPORT.json]

Every key of the union is compared exactly; there is no tolerance (the values are counts, flags,
names, FindPass indices, 8-bit colours, logs and exception texts). Each missing, extra and
differing key is listed with both values, and the report keeps them all. Exception keys
(`key.exception`) and log keys (`block.log`) are ordinary keys of the union: a case that throws
on one side and answers on the other is one missing and one extra key. Groups summarize the
differences by data group (context, setup, decl.<shader>, case.<case>.<shader>, cleanup,
capture); context differences are counted like every other difference.
"""
import argparse
import json
from collections import Counter
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from guards import KEYS  # noqa: E402


def load(path):
    path = Path(path)
    if path.is_dir():
        path = path/'values.jsonl'
    values = {}
    for number, line in enumerate(path.read_text().splitlines(), 1):
        if not line.strip():
            continue
        row = json.loads(line)
        if row['k'] in values:
            raise SystemExit(f'{path}:{number}: duplicate key {row["k"]}')
        values[row['k']] = row['v']
    return values


def group(key):
    parts = key.split('.')
    if parts[0] == 'decl' and len(parts) > 1:
        return 'decl.'+parts[1]
    if parts[0] == 'case' and len(parts) > 2 and parts[2] in KEYS:
        return f'case.{parts[1]}.{parts[2]}'
    if parts[0] == 'case' and len(parts) > 1:
        return 'case.'+parts[1]
    return parts[0]


def compare(want, got):
    missing = sorted(k for k in want if k not in got)
    extra = sorted(k for k in got if k not in want)
    differ = sorted(k for k in want if k in got and got[k] != want[k])
    return missing, extra, differ


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--report')
    args = parser.parse_args()
    want, got = load(args.reference), load(args.capture)
    missing, extra, differ = compare(want, got)
    for k in missing:
        print(f'missing {k}\n     unity   {json.dumps(want[k])}')
    for k in extra:
        print(f'extra   {k}\n     unbound {json.dumps(got[k])}')
    for k in differ:
        print(f'differ  {k}\n     unity   {json.dumps(want[k])}\n     unbound {json.dumps(got[k])}')
    groups = Counter(group(k) for k in missing+extra+differ)
    for name, count in sorted(groups.items()):
        print(f'  {name}: {count}')
    total = len(set(want) | set(got))
    failing = len(missing)+len(extra)+len(differ)
    print(f'{failing} of {total} keys fail ({len(missing)} missing, {len(extra)} extra, {len(differ)} differ)')
    if args.report:
        Path(args.report).write_text(json.dumps({
            'reference': str(args.reference), 'capture': str(args.capture), 'keys': total, 'failing': failing,
            'passing': total-failing, 'tolerance': 'exact',
            'missing': {k: want[k] for k in missing}, 'extra': {k: got[k] for k in extra},
            'differ': {k: {'unity': want[k], 'unbound': got[k]} for k in differ},
            'groups': dict(sorted(groups.items())),
        }, indent=1)+'\n')
    return 1 if failing else 0


if __name__ == '__main__':
    raise SystemExit(main())
