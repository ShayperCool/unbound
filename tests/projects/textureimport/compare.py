#!/usr/bin/env python3
"""Compares a texture import capture with the Editor's: every texture's size, mip count and
format exactly, and every byte of every mip level within --tolerance (default 0: exact). Lists
each missing, extra or differing value with its largest difference, the number of differing
bytes and the first differing texel; exits non-zero on any.

    compare.py Reference/unity-6000.0.71f1/Gamma.jsonl <capture>/Gamma.jsonl [--tolerance N]
"""
import argparse
import base64
import json
import sys


def load(path):
    rows = [json.loads(line) for line in open(path, encoding='utf-8') if line.strip()]
    values = {row['k']: row['v'] for row in rows}
    if len(values) != len(rows) or not rows:
        sys.exit(f'{path}: empty or duplicate keys')
    return values


def mip(value):
    size, data = value.split(':', 1)
    width, height = map(int, size.split('x'))
    return width, height, base64.b64decode(data)


def compare(reference, actual, tolerance):
    a, b = load(reference), load(actual)
    failures = 0
    mips = exact_mips = values = exact_values = 0
    for key in sorted(a.keys() | b.keys()):
        if key not in b:
            print('missing', key)
            failures += 1
            continue
        if key not in a:
            print('extra', key)
            failures += 1
            continue
        if key.endswith('.info'):
            if a[key] != b[key]:
                print('differs', key, 'unity', a[key], 'unbound', b[key])
                failures += 1
            continue
        (wa, ha, da), (wb, hb, db) = mip(a[key]), mip(b[key])
        mips += 1
        if (wa, ha, len(da)) != (wb, hb, len(db)):
            print('differs', key, f'size unity {wa}x{ha} unbound {wb}x{hb}')
            failures += 1
            continue
        diffs = [abs(x - y) for x, y in zip(da, db)]
        values += len(diffs)
        exact_values += diffs.count(0)
        worst = max(diffs)
        if worst == 0:
            exact_mips += 1
            continue
        first = next(i for i, d in enumerate(diffs) if d)
        texel = first // 4
        within = '' if worst > tolerance else ' (within tolerance)'
        print(f'differs {key}: max {worst}, {sum(d > tolerance for d in diffs)} bytes over '
              f'{tolerance}, {sum(1 for d in diffs if d)} differ; first texel ({texel % wa}, {texel // wa}) '
              f'unity {list(da[texel * 4:texel * 4 + 4])} unbound {list(db[texel * 4:texel * 4 + 4])}{within}')
        if worst > tolerance:
            failures += 1
    print(f'{mips} mips: {exact_mips} exact; {exact_values}/{values} bytes exact; '
          f'{failures} differences over tolerance {tolerance}')
    return int(bool(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('actual')
    parser.add_argument('--tolerance', type=int, default=0)
    args = parser.parse_args()
    return compare(args.reference, args.actual, args.tolerance)


if __name__ == '__main__':
    sys.exit(main())
