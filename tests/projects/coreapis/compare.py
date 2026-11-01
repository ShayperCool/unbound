#!/usr/bin/env python3
"""Compare a core API probe record with the reference, line by line, in order.

Each line is one event (frame, scenario, object, message; or a render-window event); events must be
equal and in the same order. Every missing (reference only) and extra (capture only) line is
kept; there is no tolerance. The optional JSON report stores the complete aligned sequence,
including every matching line.
"""
import argparse
import difflib
import json
from pathlib import Path


def load(path):
    lines = [line for line in Path(path).read_text().splitlines() if line.strip()]
    for line in lines:
        json.loads(line)
    if not lines:
        raise ValueError(f'{path}: empty capture')
    return lines


def compare(reference, actual):
    rows = []
    matcher = difflib.SequenceMatcher(a=reference, b=actual, autojunk=False)
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == 'equal':
            rows += [{'status': 'pass', 'line': reference[i], 'reference_index': i, 'actual_index': j1 + k}
                     for k, i in enumerate(range(i1, i2))]
            continue
        rows += [{'status': 'missing', 'line': reference[i], 'reference_index': i} for i in range(i1, i2)]
        rows += [{'status': 'extra', 'line': actual[j], 'actual_index': j} for j in range(j1, j2)]
    return rows


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('reference'); p.add_argument('actual'); p.add_argument('--report')
    args = p.parse_args()
    reference, actual = load(args.reference), load(args.actual)
    rows = compare(reference, actual)
    summary = {s: sum(r['status'] == s for r in rows) for s in ('pass', 'missing', 'extra')}
    summary['reference_lines'] = len(reference)
    summary['actual_lines'] = len(actual)
    if args.report:
        Path(args.report).write_text(json.dumps({'summary': summary, 'sequence': rows}, indent=1) + '\n')
    for r in rows:
        if r['status'] != 'pass':
            print(('-' if r['status'] == 'missing' else '+'), r['line'])
    print(json.dumps(summary, sort_keys=True))
    return int(summary['missing'] or summary['extra'])


if __name__ == '__main__':
    raise SystemExit(main())
