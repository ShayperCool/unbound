#!/usr/bin/env python3
"""Compares a texture array capture with the Editor's: every info line and every error exactly,
and every byte of every pixel value (GetPixels32, GetPixels floats, GetPixelData, GPU samples)
within --tolerance (default 0: exact). Lists each missing, extra or differing value with its
largest difference, the number of differing bytes and the first differing byte; exits non-zero on
any. A summary per value kind (px32, px, raw, sample) follows.

    compare.py Reference/unity-6000.0.71f1/Gamma.jsonl <capture>/Gamma.jsonl [--tolerance N]
"""
import argparse
import base64
import json
import re
import sys

PIXELS = re.compile(r'^(\d+x\d+(?:x\d+)?):(.*)$', re.S)


def load(path):
    rows = [json.loads(line) for line in open(path, encoding='utf-8') if line.strip()]
    values = {row['k']: row['v'] for row in rows}
    if len(values) != len(rows) or not rows:
        sys.exit(f'{path}: empty or duplicate keys')
    return values


def kind(key):
    if '.sample' in key:
        return 'sample'
    return key.rsplit('.', 1)[-1]


def compare(reference, actual, tolerance):
    a, b = load(reference), load(actual)
    failures = 0
    stats = {}
    for key in sorted(a.keys() | b.keys()):
        if key not in b:
            print('missing', key)
            failures += 1
            continue
        if key not in a:
            print('extra', key)
            failures += 1
            continue
        k = kind(key)
        s = stats.setdefault(k, [0, 0, 0, 0])
        s[0] += 1
        pa, pb = PIXELS.match(a[key]), PIXELS.match(b[key])
        if not pa or not pb:
            if a[key] == b[key]:
                s[1] += 1
            else:
                print('differs', key, 'unity', a[key][:300], 'unbound', b[key][:300])
                failures += 1
            continue
        da, db = base64.b64decode(pa.group(2)), base64.b64decode(pb.group(2))
        if pa.group(1) != pb.group(1) or len(da) != len(db):
            print('differs', key, f'size unity {pa.group(1)} ({len(da)} bytes) unbound {pb.group(1)} ({len(db)} bytes)')
            failures += 1
            continue
        diffs = [abs(x - y) for x, y in zip(da, db)]
        s[2] += len(diffs)
        s[3] += diffs.count(0)
        worst = max(diffs, default=0)
        if worst == 0:
            s[1] += 1
            continue
        first = next(i for i, d in enumerate(diffs) if d)
        within = '' if worst > tolerance else ' (within tolerance)'
        print(f'differs {key}: max {worst}, {sum(d > tolerance for d in diffs)} bytes over {tolerance}, '
              f'{sum(1 for d in diffs if d)} differ; first byte {first} unity {list(da[first:first + 4])} '
              f'unbound {list(db[first:first + 4])}{within}')
        if worst > tolerance:
            failures += 1
    for k in sorted(stats):
        n, exact, values, exact_values = stats[k]
        print(f'{k}: {exact}/{n} exact; {exact_values}/{values} bytes exact')
    print(f'{failures} differences over tolerance {tolerance}')
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
