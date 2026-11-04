#!/usr/bin/env python3
"""Compare the manual Destroy contract probe: Unity's reference against a capture.

    compare.py REFERENCE.jsonl CAPTURE_DIR_OR_values.jsonl [--report REPORT.json]

Every key of the union is compared exactly; there is no tolerance (the values are booleans,
labels, counts, probe steps, relative frames, bracket positions and exception type names). Each
missing, extra and differing key is listed with both values, and the report keeps them all. For
a differing state read (a dict) the differing fields are also listed; for a differing trace or
order (a list) the first differing index is listed. Lists are compared in their recorded order;
nothing is sorted. Groups summarize the differences by data group (context, setup, loop,
snap.<subject>, case.<case> (a wfeof case's issued flag included), points, trace.<subject>,
order, logs, faults, steps).
"""
import argparse
import json
from collections import Counter
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from guards import LABELS, CASES  # noqa: E402

CASE_NAMES = tuple(c[0] for c in CASES)


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


def subject_of(rest):
    return next((label for label in LABELS if rest == label), rest)


def group(key):
    parts = key.split('.')
    if parts[0] == 'snap' and len(parts) > 3:
        return 'snap.'+subject_of('.'.join(parts[3:]))
    if parts[0] == 'case':
        rest = key[len('case.'):]
        name = next((n for n in sorted(CASE_NAMES, key=len, reverse=True) if rest.startswith(n+'.')), rest)
        return 'case.'+name
    if parts[0] == 'trace':
        return 'trace.'+subject_of(key[len('trace.'):])
    return parts[0]


def detail(want, got):
    """Where two differing values differ (fields of a dict, first index of a list)."""
    if isinstance(want, dict) and isinstance(got, dict):
        fields = []
        for k in list(want)+[k for k in got if k not in want]:
            if want.get(k, '<missing>') != got.get(k, '<missing>'):
                fields.append(k if not isinstance(want.get(k), dict) or not isinstance(got.get(k), dict)
                              else k+'.'+','.join(detail(want[k], got[k])['fields']))
        return {'fields': fields}
    if isinstance(want, list) and isinstance(got, list):
        first = next((i for i, (a, b) in enumerate(zip(want, got)) if a != b), min(len(want), len(got)))
        return {'first_index': first, 'lengths': [len(want), len(got)]}
    return {}


def compare(want, got):
    missing = [k for k in want if k not in got]
    extra = [k for k in got if k not in want]
    differ = [k for k in want if k in got and got[k] != want[k]]
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
        print(f'missing {k}\n     unity   {json.dumps(want[k])[:400]}')
    for k in extra:
        print(f'extra   {k}\n     capture {json.dumps(got[k])[:400]}')
    for k in differ:
        print(f'differ  {k} {json.dumps(detail(want[k], got[k]))}\n     unity   {json.dumps(want[k])[:400]}'
              f'\n     capture {json.dumps(got[k])[:400]}')
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
            'differ': {k: {'unity': want[k], 'capture': got[k], 'where': detail(want[k], got[k])} for k in differ},
            'groups': dict(sorted(groups.items())),
        }, indent=1)+'\n')
    return 1 if failing else 0


if __name__ == '__main__':
    raise SystemExit(main())
