#!/usr/bin/env python3
"""Compares two lightmap UV captures mesh by mesh, exactly (float32 values at tolerance 0):

    compare.py <reference.jsonl> <candidate.jsonl> [--report out.json] [--uv2]

Every (variant, mesh) of either side is listed with each field that is missing or differs: the
vertex count, sub-mesh triangles, vertices, normals, UV and UV2 by index (the count of differing
values and the largest difference), and `uv2Corners`: the UV2 of every triangle corner, the
triangles found by their corners' positions and UVs (so independent of the order of triangles,
vertices and sub-meshes), with the triangles either side lacks and the corners whose UV2
differs. Exits 1 when anything differs; with --uv2, when the vertex count or `uv2Corners`
differs.
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


def corners(row):
    """Triangle (its corners' position and UV, rotated to start at the smallest) → corner UV2s."""
    out = {}
    for submesh in row['subMeshes']:
        for t in zip(submesh[::3], submesh[1::3], submesh[2::3]):
            key = [(tuple(map(f32, row['vertices'][i])), tuple(map(f32, row['uv'][i]))) for i in t]
            uv2 = [tuple(map(f32, row['uv2'][i])) if row['uv2'] else None for i in t]
            k = key.index(min(key))
            out.setdefault(tuple(key[k:] + key[:k]), []).append(tuple(uv2[k:] + uv2[:k]))
    return out


def corner_diff(expected, actual):
    a, b = corners(expected), corners(actual)
    missing = [k for k in a if k not in b]
    extra = [k for k in b if k not in a]
    differing = [(k, x, y) for k in a if k in b for x, y in zip(sorted(a[k], key=repr), sorted(b[k], key=repr))
                 for i in range(3) if x[i] != y[i]]
    if not missing and not extra and not differing:
        return None
    worst = max((abs(x[i][j] - y[i][j]) for _, x, y in differing for i in range(3) if x[i] and y[i]
                 for j in range(2)), default=None)
    return {'triangles': sum(map(len, a.values())), 'missing_triangles': len(missing),
            'extra_triangles': len(extra), 'differing_corners': len(differing), 'max_abs': worst,
            'first': [[list(k[0][0]), list(x[0] or []), list(y[0] or [])] for k, x, y in differing[:2]]}


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
    parser.add_argument('--uv2', action='store_true', help='fail only on the vertex count and uv2Corners')
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
            if (d := corner_diff(expected[key], actual[key])) is not None:
                fields['uv2Corners'] = d
            report[name] = fields or 'match'
        verdict = report[name]
        if isinstance(verdict, dict) and args.uv2:
            verdict = {f: d for f, d in verdict.items() if f in ('vertexCount', 'uv2Corners')} or 'match'
        if verdict != 'match':
            failing += 1
        print(name, json.dumps(report[name]))
    print(f"{len(report) - failing}/{len(report)} meshes match{' (vertex count and UV2 per corner)' if args.uv2 else ''}")
    if args.report:
        with open(args.report, 'w') as out:
            json.dump(report, out, indent=1)
    return int(failing > 0)


if __name__ == '__main__':
    sys.exit(main())
