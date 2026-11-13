#!/usr/bin/env python3
"""Compares a texture import capture with the Editor's: every texture's size, mip count and
format exactly, and every byte of every mip level within --tolerance (default 0: exact). Lists
each missing, extra or differing value with its largest difference, the number of differing
bytes and the first differing texel; exits non-zero on any.

    compare.py Reference/unity-6000.0.71f1/Gamma.jsonl <capture>/Gamma.jsonl [--tolerance N]
    compare.py ... --overlay Reference/unity-6000.0.71f1/macOS.jsonl   # the macOS Editor's values

An overlay holds the 4-byte words in which another OS's Editor differs from the reference, per
profile (the reference file's name) and key: rows {"profile", "k", "words": {index: "0x<u32>"}},
each word little-endian. The comparison then uses that Editor's values.
"""
import argparse
import base64
import json
from pathlib import Path
import sys


def load(path):
    rows = [json.loads(line) for line in open(path, encoding='utf-8') if line.strip()]
    values = {row['k']: row['v'] for row in rows}
    if len(values) != len(rows) or not rows:
        sys.exit(f'{path}: empty or duplicate keys')
    return values


def apply_overlay(values, overlay, profile):
    """The reference values with the overlay's words of `profile` in place."""
    rows = [json.loads(line) for line in open(overlay, encoding='utf-8') if line.strip()]
    for row in (row for row in rows if row['profile'] == profile):
        if row['k'] not in values:
            sys.exit(f'{overlay}: {profile} {row["k"]} is not in the reference')
        width, height, data = mip(values[row['k']])
        data = bytearray(data)
        for index, word in row['words'].items():
            if not 0 <= int(index) < len(data) // 4:
                sys.exit(f'{overlay}: {profile} {row["k"]} has no word {index}')
            data[int(index) * 4:int(index) * 4 + 4] = int(word, 16).to_bytes(4, 'little')
        values[row['k']] = f'{width}x{height}:' + base64.b64encode(bytes(data)).decode()
    return values


def mip(value):
    size, data = value.split(':', 1)
    width, height = map(int, size.split('x'))
    return width, height, base64.b64decode(data)


def compare(reference, actual, tolerance, overlay=None):
    a, b = load(reference), load(actual)
    if overlay:
        a = apply_overlay(a, overlay, Path(reference).stem)
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
    parser.add_argument('--overlay', help="another OS's Editor values (see above)")
    args = parser.parse_args()
    return compare(args.reference, args.actual, args.tolerance, args.overlay)


if __name__ == '__main__':
    sys.exit(main())
