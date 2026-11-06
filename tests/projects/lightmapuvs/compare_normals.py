#!/usr/bin/env python3
"""Compare the Norm* records, retaining every difference in a report in the run directory.

The original exact comparator remains available as compare.py. This gate classifies the
explicitly recorded float residuals separately from exact matches; topology stays exact.
"""
import argparse
import json
from pathlib import Path
import struct

FIELDS = ('vertexCount', 'subMeshes', 'vertices', 'normals', 'uv', 'uv2')
RESIDUAL_ASSETS = {'Faces', 'Grid', 'Ico', 'Organic', 'Sphere', 'Torus'}
RESIDUAL_VARIANTS = {'NormAngle', 'NormArea', 'NormCalc', 'NormCalc30', 'NormCalc180',
                     'NormFromAngle', 'NormGroups', 'NormNoSplit', 'NormUnweighted'}


def f32(x):
    return struct.unpack('<f', struct.pack('<f', x))[0]


def load(path):
    rows = {}
    for line in Path(path).read_text().splitlines():
        row = json.loads(line)
        if not row['variant'].startswith('Norm'):
            continue
        key = row['variant'], row['mesh']
        if key in rows:
            raise ValueError(f'duplicate key {key} in {path}')
        rows[key] = row
    if not rows:
        raise ValueError(f'no Norm* records in {path}')
    return rows


def differences(a, b, path=()):
    if isinstance(a, list) and isinstance(b, list):
        out = []
        for i in range(max(len(a), len(b))):
            p = path + (i,)
            if i >= len(a):
                out.append({'index': p, 'extra': b[i]})
            elif i >= len(b):
                out.append({'index': p, 'missing': a[i]})
            else:
                out.extend(differences(a[i], b[i], p))
        return out
    if (a == b if isinstance(a, int) and isinstance(b, int) else f32(a) == f32(b)):
        return []
    return [{'index': path, 'expected': a, 'actual': b}]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference')
    parser.add_argument('candidate')
    parser.add_argument('--expect', help='comma-separated source assets gated; every case is reported')
    args = parser.parse_args()
    expected, actual = load(args.reference), load(args.candidate)
    selected = set(args.expect.split(',')) if args.expect else None
    unknown = (selected or set()) - {key[1].split('/')[0] for key in expected}
    if unknown:
        parser.error(f'unknown expected source assets: {sorted(unknown)}')
    report, failures, exact, residuals = {}, 0, 0, 0
    for key in sorted(expected.keys() | actual.keys()):
        gated = selected is None or key[1].split('/')[0] in selected
        if key not in actual:
            entry = {'missing': expected[key]}
        elif key not in expected:
            entry = {'extra': actual[key]}
        else:
            entry = {field: diff for field in FIELDS
                     if (diff := differences(expected[key][field], actual[key][field]))}
        if not entry:
            exact += 1
            status = 'exact'
        elif (set(entry) == {'normals'} and key[0] in RESIDUAL_VARIANTS
              and key[1].split('/')[0] in RESIDUAL_ASSETS) and all(
                'expected' in d and 'actual' in d and abs(f32(d['expected']) - f32(d['actual'])) <= 1.8e-7
                for d in entry['normals']):
            residuals += 1
            status = 'recorded normal residual'
        else:
            status = 'FAIL'
            failures += gated
        report['/'.join(key)] = {'status': status, 'gated': gated, 'differences': entry}
        if status != 'exact':
            print('/'.join(key), status, ','.join(entry))
    output = Path(args.candidate).with_name('normal-comparison.json')
    output.write_text(json.dumps(report, indent=1) + '\n')
    print(f'normals: {exact}/{len(report)} cases exact, {residuals} recorded residuals, {failures} gated failures; {output}')
    return int(failures > 0)


if __name__ == '__main__':
    raise SystemExit(main())
