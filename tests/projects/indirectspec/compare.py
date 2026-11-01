#!/usr/bin/env python3
"""Compares one capture of the unity_IndirectSpecColor witness with a profile's Reference.

    compare.py REFERENCE.jsonl CAPTURE.jsonl --profile linear|gamma [--report REPORT.json]

Strict: a key passes only if its value is the Reference's exact string. Every key of either
side is kept: missing, extra and duplicate keys fail, and metadata the engine records
differently (device type, serialized properties, ...) is a failing key like any other. Nothing is
filtered, normalized or toleranced. For values whose tokens are all numbers on both sides, the
report also holds the largest absolute token difference (information only: it never turns a
key green). The witness's own output checks (gate.output_problems: every predeclared key,
finite values, one completion marker, last) are recorded alongside. Exit 0 only if every key
passes.
"""
import argparse
import json
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gate  # noqa: E402


def read(path):
    """(values by key, duplicate keys, unparsable line numbers) in file order."""
    values, duplicates, bad = {}, [], []
    for number, line in enumerate(Path(path).read_text().splitlines(), 1):
        if not line.strip():
            continue
        try:
            record = json.loads(line)
        except json.JSONDecodeError:
            bad.append(number)
            continue
        key, value = record.get('k'), record.get('v')
        if not isinstance(key, str):
            bad.append(number)
            continue
        if key in values:
            duplicates.append(key)
        values[key] = value
    return values, duplicates, bad


def numbers(value):
    if not isinstance(value, str):
        return [float(value)] if isinstance(value, (int, float)) and not isinstance(value, bool) else None
    try:
        out = [float(t) for t in value.split()]
    except ValueError:
        return None
    return out or None


def max_abs(a, b):
    x, y = numbers(a), numbers(b)
    if x is None or y is None or len(x) != len(y):
        return None
    worst = 0.0
    for p, q in zip(x, y):
        if math.isnan(p) or math.isnan(q):
            if not (math.isnan(p) and math.isnan(q)):
                return math.inf
            continue
        worst = max(worst, abs(p - q))
    return worst


def compare(reference, capture, profile):
    ref, ref_dup, ref_bad = read(reference)
    out, out_dup, out_bad = read(capture)
    rows = []
    for key in list(ref) + [k for k in out if k not in ref]:
        if key not in out:
            rows.append(dict(key=key, status='missing', reference=ref[key]))
        elif key not in ref:
            rows.append(dict(key=key, status='extra', capture=out[key]))
        else:
            same = json.dumps(ref[key]) == json.dumps(out[key])
            row = dict(key=key, status='pass' if same else 'differ', reference=ref[key], capture=out[key])
            if not same:
                row['max_abs'] = max_abs(ref[key], out[key])
            rows.append(row)
    counts = {s: sum(r['status'] == s for r in rows) for s in ('pass', 'differ', 'missing', 'extra')}
    try:
        output_problems = gate.output_problems(Path(capture).parent, profile) \
            if Path(capture).name == gate.OUT else [f'witness checks need a capture named {gate.OUT}']
    except Exception as e:  # recorded, never a reason to skip the comparison
        output_problems = [f'output check failed: {type(e).__name__}: {e}']
    ok = counts['pass'] == len(rows) and not out_dup and not out_bad
    return dict(reference=str(reference), capture=str(capture), profile=profile, keys=len(rows), reference_keys=len(ref),
                capture_keys=len(out), counts=counts, capture_duplicates=out_dup, capture_unparsable_lines=out_bad,
                reference_duplicates=ref_dup, reference_unparsable_lines=ref_bad,
                witness_output_problems=output_problems, ok=ok, rows=rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--profile', required=True, choices=sorted(gate.COLOR))
    parser.add_argument('--report')
    args = parser.parse_args()
    report = compare(args.reference, args.capture, args.profile)
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=1) + '\n')
    c = report['counts']
    print(f"{c['pass']}/{report['keys']} pass, {c['differ']} differ, {c['missing']} missing, {c['extra']} extra, "
          f"{len(report['capture_duplicates'])} duplicate, {len(report['capture_unparsable_lines'])} unparsable")
    for row in report['rows']:
        if row['status'] != 'pass':
            print(f"  {row['status']:7} {row['key']}" + (f"  max_abs {row['max_abs']}" if row.get('max_abs') is not None else ''))
    return 0 if report['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
