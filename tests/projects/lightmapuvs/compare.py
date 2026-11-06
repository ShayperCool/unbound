#!/usr/bin/env python3
"""Compares two lightmap UV captures mesh by mesh, exactly (float32 values at tolerance 0):

    compare.py <reference.jsonl> <candidate.jsonl> [--report out.json]

Every (variant, mesh) of either side is listed with each field that is missing or differs: the
vertex count, sub-mesh triangles, vertices, normals, UV and UV2 (the count of differing values
and the largest difference). Exits 1 when anything differs.
"""
import argparse
import json
import struct
import sys

FIELDS = ('vertexCount', 'subMeshes', 'vertices', 'normals', 'uv', 'uv2')


def load(path):
    rows = {}
    for line in open(path):
        if line.strip():
            row = json.loads(line)
            rows[(row['variant'], row['mesh'])] = row
    return rows


def f32(value):
    return struct.unpack('<f', struct.pack('<f', value))[0]


def flat(value):
    if isinstance(value, list):
        for item in value:
            yield from flat(item)
    else:
        yield value


def diff(field, expected, actual):
    if field in ('vertexCount', 'subMeshes'):
        return None if expected == actual else {'expected': expected if field == 'vertexCount' else
                                                [len(s) for s in expected],
                                                'actual': actual if field == 'vertexCount' else
                                                [len(s) for s in actual]}
    if len(expected) != len(actual):
        return {'expected_length': len(expected), 'actual_length': len(actual)}
    a, b = list(flat(expected)), list(flat(actual))
    differing = [(i, x, y) for i, (x, y) in enumerate(zip(a, b)) if f32(x) != f32(y)]
    if not differing:
        return None
    return {'differing_values': len(differing), 'of': len(a),
            'max_abs': max(abs(x - y) for _, x, y in differing), 'first': differing[:3]}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('candidate')
    parser.add_argument('--report')
    args = parser.parse_args()
    expected, actual = load(args.reference), load(args.candidate)
    report, failing = {}, 0
    for key in sorted(set(expected) | set(actual)):
        name = '/'.join(key)
        if key not in actual:
            report[name] = 'missing'
        elif key not in expected:
            report[name] = 'extra'
        else:
            fields = {f: d for f in FIELDS if (d := diff(f, expected[key][f], actual[key][f])) is not None}
            report[name] = fields or 'match'
        if report[name] != 'match':
            failing += 1
        print(name, json.dumps(report[name]))
    print(f'{len(report) - failing}/{len(report)} meshes match')
    if args.report:
        with open(args.report, 'w') as out:
            json.dump(report, out, indent=1)
    return int(failing > 0)


if __name__ == '__main__':
    sys.exit(main())
