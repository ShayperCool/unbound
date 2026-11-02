#!/usr/bin/env python3
"""Compares a capture of the light probe scene-unload witness with Reference lifecycle.jsonl.

    compare_lifecycle.py REFERENCE.jsonl CAPTURE.jsonl [--report REPORT.json]

Strict and positional: row i passes only if it is the Reference's row i, byte for byte after JSON
parsing (sequence index, step, event, arguments and the whole recorded state). Every row of either
side is kept; rows beyond the other side's end are missing/extra. Nothing is filtered, reordered
or toleranced. As information only, the report also aligns the two event sequences
(step, event, arguments; difflib) so a moved event shows as one insertion and one deletion
instead of a shifted tail, and for each row lists which of its fields differ.
Exit 0 only if every row passes.
"""
import argparse
import difflib
import json
from pathlib import Path
import sys

FIELDS = ('seq', 'step', 'at', 'args', 'v')


def read(path):
    rows = []
    for number, line in enumerate(Path(path).read_text().splitlines(), 1):
        if not line.strip():
            continue
        try:
            rows.append(json.loads(line))
        except json.JSONDecodeError:
            rows.append({'unparsable': line, 'line': number})
    return rows


def event(row):
    return '|'.join(str(row.get(f)) for f in ('step', 'at', 'args')) if 'seq' in row else json.dumps(row, sort_keys=True)


def compare(reference, capture):
    ref, out = read(reference), read(capture)
    rows = []
    for i in range(max(len(ref), len(out))):
        a = ref[i] if i < len(ref) else None
        b = out[i] if i < len(out) else None
        if b is None:
            rows.append(dict(index=i, status='missing', reference=a))
        elif a is None:
            rows.append(dict(index=i, status='extra', capture=b))
        elif a == b:
            rows.append(dict(index=i, status='pass'))
        else:
            fields = sorted({*a, *b} - {f for f in {*a, *b} if a.get(f) == b.get(f)})
            rows.append(dict(index=i, status='differ', fields=fields, reference=a, capture=b))
    ea, eb = [event(r) for r in ref], [event(r) for r in out]
    alignment = [dict(op=op, reference=ea[i1:i2], capture=eb[j1:j2], reference_rows=[i1, i2], capture_rows=[j1, j2])
                 for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, ea, eb, autojunk=False).get_opcodes() if op != 'equal']
    counts = {s: sum(r['status'] == s for r in rows) for s in ('pass', 'differ', 'missing', 'extra')}
    return dict(reference=str(reference), capture=str(capture), reference_rows=len(ref), capture_rows=len(out),
                rows_total=len(rows), counts=counts, complete_marker_last=bool(out) and out[-1] == {'k': 'complete', 'v': True},
                event_alignment=alignment, ok=counts['pass'] == len(rows), rows=rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--report')
    args = parser.parse_args()
    report = compare(args.reference, args.capture)
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=1) + '\n')
    c = report['counts']
    print(f"{c['pass']}/{report['rows_total']} rows pass, {c['differ']} differ, {c['missing']} missing, {c['extra']} extra; "
          f"completion marker last: {report['complete_marker_last']}")
    for row in report['rows']:
        if row['status'] != 'pass':
            print(f"  row {row['index']:2} {row['status']:7} {' '.join(row.get('fields', []))}")
    for a in report['event_alignment']:
        print(f"  events {a['op']}: reference {a['reference_rows']} {a['reference']} / capture {a['capture_rows']} {a['capture']}")
    return 0 if report['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
