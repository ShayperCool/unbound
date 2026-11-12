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

CRUNCHED = {'DXT1Crunched', 'DXT5Crunched', 'ETC_RGB4Crunched', 'ETC2_RGBA8Crunched'}
BLOCKS.update({name: (4, 8 if name in ('DXT1Crunched', 'ETC_RGB4Crunched') else 16)
               for name in CRUNCHED})


def logical_blocks(width, height, mips, footprint):
    return sum(((max(1, width >> mip) + footprint - 1) // footprint)
               * ((max(1, height >> mip) + footprint - 1) // footprint) for mip in range(mips))


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
    runtime_modes = defaultdict(lambda: [0, 0])
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
        footprint, size = BLOCKS[fields[3]]
        width, height = map(int, fields[0].split('x'))
        total = logical_blocks(width, height, int(fields[2]), footprint)
        expected = base64.b64decode(reference[raw_key], validate=True)
        actual = base64.b64decode(candidate.get(raw_key, ''), validate=True)
        actual_info = candidate.get(key, '').split()
        layout = len(actual_info) >= 4 and actual_info[:4] == fields[:4] and len(actual) == len(expected)
        archive = fields[3] in CRUNCHED
        if archive:
            # Archive bytes are not GPU blocks. An identical archive verifies every logical
            # block; absent compressed storage verifies none. Different archives need a
            # transcoded-block comparison, so report only the verified lower bound.
            exact = total if layout and actual == expected else 0
        else:
            if len(expected) != total * size:
                raise ValueError(f'{raw_key}: {len(expected)} bytes for {total} blocks of {size}')
            exact = sum(expected[i:i+size] == actual[i:i+size]
                        for i in range(0, len(expected), size)) if layout else 0
        mode = prefix.rsplit('.', 1)[-1]
        group = 'imports' if prefix.startswith('tex.') else (
            'compress' if prefix.startswith('compress.') and mode in ('fast', 'high') else 'runtime')
        totals[group, fields[3]][0] += exact
        totals[group, fields[3]][1] += total
        if group == 'imports':
            quality = cases[prefix.split('.')[1]]
            qualities[fields[3], quality][0] += exact
            qualities[fields[3], quality][1] += total
        elif group == 'compress':
            runtime_modes[fields[3], mode][0] += exact
            runtime_modes[fields[3], mode][1] += total
        print(f'blocks {prefix} {fields[3]} {exact}/{total} exact'
              + (' (archive differs; exact blocks unverified)' if archive and layout and actual != expected
                 else '' if layout else ' (compressed storage, dimensions, mip count or byte length differs)'))
    for (group, fmt), (exact, total) in sorted(totals.items()):
        print(f'blocks total {group} {fmt} {exact}/{total} exact, {total-exact} residual')
    for (fmt, quality), (exact, total) in sorted(qualities.items()):
        print(f'blocks quality {fmt} q{quality} {exact}/{total} exact, {total-exact} residual')
    for (fmt, mode), (exact, total) in sorted(runtime_modes.items()):
        print(f'blocks runtime-quality {fmt} {mode} {exact}/{total} exact, {total-exact} residual')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('candidate', type=Path)
    args = parser.parse_args()
    report(read(args.reference), read(args.candidate))
