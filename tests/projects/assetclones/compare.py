#!/usr/bin/env python3
"""Compare the complete key union, retaining every absent, extra and differing value.

Metadata and raw bytes are exact. Color components allow 1e-6 on CPU and 0.002
on GPU for float attachment conversion. These fixed tolerances apply to all runs.
The optional JSON report stores the entire union, including every passing row.
"""
import argparse
import json
import math
from pathlib import Path


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    result = {r['k']: r['v'] for r in rows}
    if len(result) != len(rows) or not rows:
        raise ValueError(f'{path}: empty capture or duplicate keys')
    return result


def compare(reference, actual):
    rows = []
    for key in sorted(reference.keys() | actual.keys()):
        row = {'key': key, 'reference': reference.get(key), 'actual': actual.get(key), 'tolerance': 0}
        if key not in reference:
            row['status'] = 'extra'
        elif key not in actual:
            row['status'] = 'missing'
        elif reference[key] == actual[key]:
            row['status'] = 'pass'
        else:
            row['status'] = 'different'
            if any(s in key for s in ('.gpu', '.cpu.', '.color.', '_after_write.')) and key[-1:] in '0123':
                row['tolerance'] = 0.002 if '.gpu' in key else 1e-6
                try:
                    a, b = float(reference[key]), float(actual[key])
                    if math.isfinite(a) and math.isfinite(b) and abs(a-b) <= row['tolerance']:
                        row['status'] = 'pass'
                except ValueError:
                    pass
        rows.append(row)
    return rows


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('reference'); p.add_argument('actual'); p.add_argument('--report')
    args = p.parse_args()
    rows = compare(load(args.reference), load(args.actual))
    summary = {s: sum(r['status'] == s for r in rows) for s in ('pass','different','missing','extra')}
    if args.report:
        Path(args.report).write_text(json.dumps({'summary': summary, 'union': rows}, indent=2)+'\n')
    for r in rows:
        if r['status'] != 'pass':
            print(json.dumps(r, sort_keys=True))
    print(json.dumps(summary, sort_keys=True))
    return int(any(r['status'] != 'pass' for r in rows))


if __name__ == '__main__':
    raise SystemExit(main())
