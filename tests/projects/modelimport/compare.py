#!/usr/bin/env python3
"""Compares a model import capture with the Editor's: every key, numbers within 1e-5
(absolute or relative), everything else exactly. Lists every missing, extra and differing
value; exits non-zero on any.

    compare.py Reference/unity-6000.0.71f1/Gamma.jsonl <capture>.jsonl
    compare.py Reference/unity-6000.0.71f1/descriptions.txt <capture>.descriptions.txt

The second form compares the material descriptions the importers handed the preprocessors
(one block per model and material; repeated identical blocks count once).
"""
import json
import math
import re
import sys


NUMBER = re.compile(r'-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?')


def descriptions(path):
    """{(model, material): {"kind name": value}}; numbers parsed, the rest as text."""
    blocks, current = {}, None
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if not line.startswith('  '):
            model, material = line.split(' | ', 1)
            current = {}
            key = (model, material)
            blocks.setdefault(key, []).append(current)
            continue
        kind_name, value = line.strip().split(' = ', 1)
        if kind_name.startswith(('float', 'vector')):
            value = [float(x) for x in NUMBER.findall(value)]
        current[kind_name] = value
    out = {}
    for key, versions in blocks.items():
        unique = [v for i, v in enumerate(versions) if v not in versions[:i]]
        if len(unique) > 1:
            sys.exit(f'{path}: {key} described differently {len(unique)} times')
        out[key] = unique[0]
    return out


def compare_descriptions(reference, actual):
    a, b = descriptions(reference), descriptions(actual)
    failures = 0
    for key in a.keys() | b.keys():
        if key not in b:
            print('missing material', key)
            failures += 1
            continue
        if key not in a:
            print('extra material', key)
            failures += 1
            continue
        x, y = a[key], b[key]
        for prop in sorted(x.keys() | y.keys()):
            if prop not in y:
                print('missing', key, prop, x[prop])
            elif prop not in x:
                print('extra', key, prop, y[prop])
            elif not equal(x[prop], y[prop]):
                print('differs', key, prop, 'unity', x[prop], 'unbound', y[prop])
            else:
                continue
            failures += 1
    print(f'{len(a)} materials, {failures} differences')
    return int(bool(failures))


def load(path):
    rows = [json.loads(line) for line in open(path, encoding='utf-8') if line.strip()]
    values = {row['k']: row['v'] for row in rows}
    if len(values) != len(rows) or not rows:
        sys.exit(f'{path}: empty or duplicate keys')
    return values


def equal(a, b):
    if isinstance(a, list):
        return isinstance(b, list) and len(a) == len(b) and all(equal(x, y) for x, y in zip(a, b))
    if isinstance(a, (int, float)) and not isinstance(a, bool):
        return (isinstance(b, (int, float)) and not isinstance(b, bool)
                and math.isclose(a, b, rel_tol=1e-5, abs_tol=1e-5))
    return a == b


def main(reference, actual):
    a, b = load(reference), load(actual)
    missing = [k for k in a if k not in b]
    extra = [k for k in b if k not in a]
    differ = [k for k in a if k in b and not equal(a[k], b[k])]
    for k in missing:
        print('missing', k, json.dumps(a[k]))
    for k in extra:
        print('extra', k, json.dumps(b[k]))
    for k in differ:
        print('differs', k, 'unity', json.dumps(a[k]), 'unbound', json.dumps(b[k]))
    print(f'{len(a) - len(missing) - len(differ)}/{len(a)} match, {len(missing)} missing, '
          f'{len(differ)} differ, {len(extra)} extra')
    return int(bool(missing or extra or differ))


if __name__ == '__main__':
    if sys.argv[1].endswith('.txt'):
        sys.exit(compare_descriptions(*sys.argv[1:3]))
    sys.exit(main(*sys.argv[1:3]))
