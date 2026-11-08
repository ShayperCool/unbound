#!/usr/bin/env python3
"""Compares a model clip capture with the Editor's reference: every record and value, numbers
within 2e-4 (absolute or relative), nested lists element by element; a rotation (a field ending
in `.r`) also matches its negation. Lists every missing, extra and differing value; exits
non-zero on any.

    compare.py Reference/unity-6000.0.71f1.jsonl <capture>/values.jsonl [-q] [--json <summary>] [--name <probe>]

Other probes with the same record format (rootcurves) use it with their own --name.
"""
import json
import math
import sys

TOLERANCE = 2e-4


def load(path):
    rows = [json.loads(line) for line in open(path, encoding='utf-8') if line.strip()]
    values = {row['k']: row['v'] for row in rows}
    if len(values) != len(rows) or not rows:
        sys.exit(f'{path}: empty or duplicate keys')
    return values


def close(a, b):
    return math.isclose(a, b, rel_tol=TOLERANCE, abs_tol=TOLERANCE)


def same(a, b):
    if isinstance(a, bool) or isinstance(b, bool):
        return a == b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return close(a, b)
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    return a == b


def same_field(field, a, b):
    if same(a, b):
        return True
    return (field.endswith('.r') and isinstance(a, list) and isinstance(b, list) and len(a) == 4
            and same(a, [-x if isinstance(x, (int, float)) else x for x in b]))


def fields(value):
    """A record's fields: an object's keys, else the record itself as one field."""
    return value if isinstance(value, dict) else {'': value}


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('-')]
    quiet = '-q' in sys.argv
    summary = sys.argv[sys.argv.index('--json') + 1] if '--json' in sys.argv else None
    if summary:
        args.remove(summary)
    name = sys.argv[sys.argv.index('--name') + 1] if '--name' in sys.argv else 'modelroot'
    if '--name' in sys.argv:
        args.remove(name)
    reference, actual = load(args[0]), load(args[1])
    values = failed_records = failed_values = 0
    lines = []
    for key in sorted(reference.keys() | actual.keys()):
        if key not in actual:
            lines.append(f'missing {key}')
            failed_records += 1
            continue
        if key not in reference:
            lines.append(f'extra {key}')
            failed_records += 1
            continue
        a, b = fields(reference[key]), fields(actual[key])
        bad = False
        for field in sorted(a.keys() | b.keys()):
            values += 1
            if field not in b or field not in a:
                lines.append(f'{key} {field}: Unity {json.dumps(a.get(field, "MISSING"))}, '
                             f'Unbound {json.dumps(b.get(field, "MISSING"))}')
            elif not same_field(field, a[field], b[field]):
                lines.append(f'{key} {field}: Unity {json.dumps(a[field])}, Unbound {json.dumps(b[field])}')
            else:
                continue
            failed_values += 1
            bad = True
        failed_records += bad
    if not quiet:
        print('\n'.join(lines))
    total = len(reference)
    print(f'{name}: {total - failed_records}/{total} records, '
          f'{values - failed_values}/{values} values match')
    if summary:
        with open(summary, 'w') as handle:
            json.dump({'records': total, 'matching_records': total - failed_records, 'values': values,
                       'matching_values': values - failed_values, 'differences': lines}, handle, indent=1)
    return int(bool(failed_records))


if __name__ == '__main__':
    sys.exit(main())
