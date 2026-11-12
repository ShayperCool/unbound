#!/usr/bin/env python3
"""Counts exact compressed blocks without ignoring any differing block or layout."""
import argparse
import base64
from collections import defaultdict
import json
from pathlib import Path

BLOCKS = {
    'DXT1': (4, 8), 'DXT5': (4, 16), 'BC4': (4, 8), 'BC5': (4, 16),
    'BC6H': (4, 16), 'BC7': (4, 16), 'ETC_RGB4': (4, 8),
    'ETC2_RGB': (4, 8), 'ETC2_RGBA1': (4, 8), 'ETC2_RGBA8': (4, 16),
    'EAC_R': (4, 8), 'EAC_RG': (4, 16),
    **{f'ASTC_{n}x{n}': (n, 16) for n in (4, 5, 6, 8, 10, 12)},
    **{f'ASTC_HDR_{n}x{n}': (n, 16) for n in (4, 5, 6, 8, 10, 12)},
}


def read(path):
    result = {}
    for line in Path(path).read_text().splitlines():
        row = json.loads(line)
        if row['k'] in result:
            raise ValueError(f'duplicate key {row["k"]}')
        result[row['k']] = row['v']
    return result


def report(reference, candidate):
    totals = defaultdict(lambda: [0, 0])
    qualities = defaultdict(lambda: [0, 0])
    cases = {}
    for line in (Path(__file__).parent/'Assets/Editor/TextureCases.txt').read_text().splitlines():
        fields = line.split()
        if fields:
            settings = dict(item.split('=', 1) for item in fields[1:])
            quality = settings.get('quality', 50)
            if settings.get('override') == '0':
                quality = settings.get('defaultQuality', quality)
            cases[fields[0]] = int(quality)
    for key, info in reference.items():
        if not key.endswith('.info'):
            continue
        fields = info.split()
        if len(fields) < 4 or fields[3] not in BLOCKS:
            continue
        prefix = key[:-5]
        raw_key = prefix + '.raw'
        if raw_key not in reference:
            continue
        size = BLOCKS[fields[3]][1]
        expected = base64.b64decode(reference[raw_key], validate=True)
        if len(expected) % size:
            raise ValueError(f'{raw_key}: incomplete reference block')
        actual = base64.b64decode(candidate.get(raw_key, ''), validate=True)
        actual_info = candidate.get(key, '').split()
        layout = len(actual_info) >= 4 and actual_info[:4] == fields[:4] and len(actual) == len(expected)
        exact = sum(expected[i:i+size] == actual[i:i+size]
                    for i in range(0, len(expected), size)) if layout else 0
        total = len(expected) // size
        group = 'imports' if prefix.startswith('tex.') else 'runtime'
        totals[group, fields[3]][0] += exact
        totals[group, fields[3]][1] += total
        if group == 'imports':
            quality = cases[prefix.split('.')[1]]
            qualities[fields[3], quality][0] += exact
            qualities[fields[3], quality][1] += total
        print(f'blocks {prefix} {fields[3]} {exact}/{total} exact'
              + ('' if layout else ' (format, dimensions, mip count or byte length differs)'))
    for (group, fmt), (exact, total) in sorted(totals.items()):
        print(f'blocks total {group} {fmt} {exact}/{total} exact, {total-exact} residual')
    for (fmt, quality), (exact, total) in sorted(qualities.items()):
        print(f'blocks quality {fmt} q{quality} {exact}/{total} exact, {total-exact} residual')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('candidate', type=Path)
    args = parser.parse_args()
    report(read(args.reference), read(args.candidate))
