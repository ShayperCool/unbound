#!/usr/bin/env python3
"""Compares two sprite import captures (reference first), value by value, exactly.

    compare.py <reference sprites.jsonl> <candidate sprites.jsonl>

Numbers compare as 32-bit floats, without tolerance: the importer's mesh generation is exact.
Every missing, extra or differing value is listed; the exit status is 1 when any is.
"""
import json
import struct
import sys

FIELDS = ('rect', 'pivot', 'ppu', 'bounds', 'textureRect', 'textureRectOffset', 'vertices', 'uv',
          'triangles', 'physics')


def f32(v):
    if isinstance(v, str):
        return v
    return struct.unpack('<f', struct.pack('<f', v))[0]


def flatten(v, path=''):
    if isinstance(v, list):
        out = [(path + '.len', len(v))]
        for i, x in enumerate(v):
            out += flatten(x, f'{path}[{i}]')
        return out
    return [(path, f32(v) if isinstance(v, (int, float)) else v)]


def load(path):
    records = {}
    for line in open(path):
        if line.strip():
            r = json.loads(line)
            records[(r['texture'], r['name'])] = r
    return records


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    total = matched = 0
    problems = []
    for key in ref:
        if key not in cand:
            problems.append(f'missing sprite {key[0]}/{key[1]}')
            total += len(FIELDS)
            continue
        for field in FIELDS:
            total += 1
            a, b = dict(flatten(ref[key][field])), dict(flatten(cand[key][field]))
            diffs = [(p, a.get(p), b.get(p)) for p in sorted(set(a) | set(b)) if a.get(p) != b.get(p)]
            if diffs:
                problems.append(f'{key[0]}/{key[1]} {field}: ' + '; '.join(
                    f'{field}{p} {x} != {y}' for p, x, y in diffs))
            else:
                matched += 1
    for key in cand:
        if key not in ref:
            problems.append(f'extra sprite {key[0]}/{key[1]}')
    for p in problems:
        print(p)
    print(f'{matched}/{total} values match ({len(ref)} reference sprites, {len(cand)} candidate sprites)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
