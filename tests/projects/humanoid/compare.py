#!/usr/bin/env python3
"""Compares a humanoid probe capture with the Editor's reference: every record and every leaf
value, numbers within 2e-4 (absolute or relative); a four-number list (a quaternion) also matches
its negation. Lists every missing, extra and differing value with a per-section summary (the key's
first two parts, e.g. `HumanA.handler`); exits non-zero on any difference.

    compare.py Reference/unity-6000.0.71f1.jsonl <capture>/values.jsonl [-q] [--json <summary>] [--section PREFIX]...
"""
import collections
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


def number(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool)


def close(a, b):
    return math.isclose(a, b, rel_tol=TOLERANCE, abs_tol=TOLERANCE)


def leaves(value, path=''):
    """(path, value) of every leaf; a four-number list stays one leaf (a quaternion)."""
    if isinstance(value, dict):
        for k in value:
            yield from leaves(value[k], f'{path}.{k}' if path else k)
    elif isinstance(value, list) and not (len(value) == 4 and all(number(x) for x in value)):
        for i, x in enumerate(value):
            yield from leaves(x, f'{path}[{i}]')
    else:
        yield path, value


def same(a, b):
    if isinstance(a, list) and isinstance(b, list) and len(a) == len(b) == 4:
        return all(close(x, y) for x, y in zip(a, b)) or all(close(x, -y) for x, y in zip(a, b))
    if number(a) and number(b):
        return close(a, b)
    return a == b


def section(key):
    return '.'.join(key.split('.')[:2])


def main():
    argv = sys.argv[1:]
    quiet = '-q' in argv
    summary = argv[argv.index('--json') + 1] if '--json' in argv else None
    sections = [argv[i + 1] for i, a in enumerate(argv) if a == '--section']
    args = [a for i, a in enumerate(argv) if not a.startswith('-') and (i == 0 or argv[i - 1] not in ('--json', '--section'))]
    reference, actual = load(args[0]), load(args[1])
    keys = sorted(reference.keys() | actual.keys())
    if sections:
        keys = [k for k in keys if any(k.startswith(s) for s in sections)]
    stats = collections.OrderedDict()
    lines = []
    for key in keys:
        s = stats.setdefault(section(key), [0, 0])
        if key not in actual or key not in reference:
            n = sum(1 for _ in leaves(reference.get(key, actual.get(key))))
            s[1] += n
            lines.append(f'{"missing" if key not in actual else "extra"} {key} ({n} values)')
            continue
        a = dict(leaves(reference[key]))
        b = dict(leaves(actual[key]))
        for field in sorted(a.keys() | b.keys()):
            if field in a:
                s[1] += 1
            if field not in b:
                lines.append(f'{key} {field}: Unity {a[field]}, Unbound MISSING')
            elif field not in a:
                lines.append(f'{key} {field}: Unity MISSING, Unbound {b[field]}')
            elif not same(a[field], b[field]):
                lines.append(f'{key} {field}: Unity {a[field]}, Unbound {b[field]}')
            else:
                s[0] += 1
    if not quiet:
        print('\n'.join(lines))
    for name, (ok, total) in stats.items():
        print(f'{name}: {ok}/{total}')
    ok = sum(s[0] for s in stats.values())
    total = sum(s[1] for s in stats.values())
    print(f'humanoid: {ok}/{total} values match, {len(lines)} differences')
    if summary:
        with open(summary, 'w') as handle:
            json.dump({'values': total, 'matching_values': ok, 'sections': stats, 'differences': lines}, handle, indent=1)
    return int(bool(lines))


if __name__ == '__main__':
    sys.exit(main())
