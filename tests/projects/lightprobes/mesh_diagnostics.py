#!/usr/bin/env python3
"""Report mesh topology separately from the strict geometry comparison.

Enumerate nondegenerate empty circumspheres with exact integer arithmetic on the recorded
float32 coordinates. A cospherical set can admit several tetrahedralizations; all such ties
remain in the report. This diagnostic does not change compare.py's ordering or tolerances.
"""
import argparse
import itertools
import json
from pathlib import Path
import struct


def det(a, b, c):
    return (a[0] * (b[1] * c[2] - b[2] * c[1])
            - a[1] * (b[0] * c[2] - b[2] * c[0])
            + a[2] * (b[0] * c[1] - b[1] * c[0]))


def empty_spheres(positions):
    ratios = [struct.unpack('<f', struct.pack('<f', float(v)))[0].as_integer_ratio()
              for v in positions.split()]
    scale = max(denominator for _, denominator in ratios)
    values = [numerator * (scale // denominator) for numerator, denominator in ratios]
    points = list(zip(values[::3], values[1::3], values[2::3]))
    cells, ties = [], []
    for ids in itertools.combinations(range(len(points)), 4):
        origin = points[ids[0]]
        u, v, w = [tuple(a - b for a, b in zip(points[i], origin)) for i in ids[1:]]
        volume = det(u, v, w)
        if volume == 0:
            continue
        u2, v2, w2 = [sum(x * x for x in edge) for edge in (u, v, w)]
        center = (det((u2, u[1], u[2]), (v2, v[1], v[2]), (w2, w[1], w[2])),
                  det((u[0], u2, u[2]), (v[0], v2, v[2]), (w[0], w2, w[2])),
                  det((u[0], u[1], u2), (v[0], v[1], v2), (w[0], w[1], w2)))
        on_sphere = []
        for i, point in enumerate(points):
            if i in ids:
                continue
            delta = tuple(a - b for a, b in zip(point, origin))
            side = sum(x * x for x in delta) * volume - sum(a * b for a, b in zip(delta, center))
            if (side < 0) != (volume < 0) and side != 0:
                break
            if side == 0:
                on_sphere.append(i)
        else:
            cells.append(ids)
            if on_sphere:
                ties.append({'cell': ids, 'other_vertices': on_sphere})
    return cells, ties


def records(path):
    return {row['key']: row['value'] for row in map(json.loads, path.read_text().splitlines())}


def cells(value):
    indices = list(map(int, value.split()))
    if len(indices) % 4:
        raise ValueError('incomplete tetrahedron')
    return {tuple(sorted(indices[i:i + 4])) for i in range(0, len(indices), 4)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('capture', type=Path)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    reference, capture = records(args.reference), records(args.capture)
    report = {}
    for label in ('main', 'combined'):
        key = label + ' mesh '
        if reference[key + 'positions'] != capture[key + 'positions']:
            raise ValueError(label + ': positions differ; cannot compare vertex identities')
        expected, ties = empty_spheres(reference[key + 'positions'])
        expected = set(expected)
        native, engine = cells(reference[key + 'indices']), cells(capture[key + 'indices'])
        report[label] = {'empty_sphere_cells': sorted(expected), 'cospherical_ties': ties,
                         'reference_missing': sorted(expected - native), 'reference_extra': sorted(native - expected),
                         'capture_missing': sorted(expected - engine), 'capture_extra': sorted(engine - expected)}
        print(label, 'empty spheres', len(expected), 'reference', len(native), 'capture', len(engine),
              'missing', sorted(expected - engine), 'extra', sorted(engine - expected), 'ties', len(ties))
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n')


if __name__ == '__main__':
    main()
