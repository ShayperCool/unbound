#!/usr/bin/env python3
"""Compares a texture compression capture with the Editor's, every value exactly.

    compare.py Reference/unity-6000.0.71f1/Gamma.jsonl <capture>/Gamma.jsonl [--residuals residuals.json --profile Gamma]

Prints each missing, extra or differing key (byte values: how many bytes differ, the largest
difference and the first differing byte; float readbacks: the largest difference), then
`N/M values match, F failing, R residuals`. --residuals names recorded differences
({profile: {key or glob: why}}, the --profile's entries): they are still printed and counted,
but do not fail the comparison. Exit status 1 when anything is missing, extra or failing outside
the residuals, or a residual entry no longer explains any difference (stale).
"""
import argparse
import base64
import binascii
import fnmatch
import json
from pathlib import Path
import struct
import sys


def load(path):
    rows = [json.loads(line) for line in open(path, encoding='utf-8') if line.strip()]
    values = {row['k']: row['v'] for row in rows}
    if len(values) != len(rows) or not rows:
        sys.exit(f'{path}: empty or duplicate keys')
    return values


def describe(key, a, b):
    """How two differing values differ."""
    if key.endswith('.gpu'):
        try:
            fa, fb = base64.b64decode(a), base64.b64decode(b)
            if len(fa) == len(fb):
                xs = struct.unpack(f'<{len(fa) // 4}f', fa)
                ys = struct.unpack(f'<{len(fb) // 4}f', fb)
                worst = max(abs(x - y) for x, y in zip(xs, ys))
                count = sum(x != y for x, y in zip(xs, ys))
                return f'{count} of {len(xs)} floats differ, largest {worst:.6g}'
            return f'{len(fa)} vs {len(fb)} bytes'
        except (binascii.Error, ValueError):
            pass
    if key.endswith('.raw') or '.pixels' in key:
        try:
            ba, bb = base64.b64decode(a, validate=True), base64.b64decode(b, validate=True)
            if len(ba) != len(bb):
                return f'{len(ba)} vs {len(bb)} bytes'
            diffs = [i for i, (x, y) in enumerate(zip(ba, bb)) if x != y]
            worst = max(abs(ba[i] - bb[i]) for i in diffs)
            return f'{len(diffs)} of {len(ba)} bytes differ, largest {worst}, first at {diffs[0]}'
        except (binascii.Error, ValueError):
            pass
    return f'{a[:160]!r} vs {b[:160]!r}'


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('actual')
    parser.add_argument('--residuals', type=Path, help='JSON {profile: {key or glob: why}} of recorded differences')
    parser.add_argument('--profile', help='the residuals entry to use')
    args = parser.parse_args()
    a, b = load(args.reference), load(args.actual)
    residuals = json.loads(args.residuals.read_text()).get(args.profile, {}) if args.residuals else {}
    failing = matched = 0
    differing = set()
    for key in sorted(a.keys() | b.keys()):
        if key not in b:
            print('missing', key)
        elif key not in a:
            print('extra', key)
        elif a[key] == b[key]:
            matched += 1
            continue
        else:
            print('differs', key, describe(key, a[key], b[key]))
        differing.add(key)
    explained = {k for k in differing if any(fnmatch.fnmatchcase(k, p) for p in residuals)}
    failing = len(differing - explained)
    stale = sorted(p for p in residuals if not any(fnmatch.fnmatchcase(k, p) for k in differing))
    for key in stale:
        print(f'stale residual {key}: it matches the reference now; remove it from the residuals')
    print(f'{matched}/{len(a)} values match, {failing} failing, {len(explained)} residuals')
    return int(bool(failing or stale))


if __name__ == '__main__':
    sys.exit(main())
