#!/usr/bin/env python3
"""Compare an rtdepth capture with a reference: the full key union and every float channel.

    compare.py REFERENCE ACTUAL [--report REPORT.json]

REFERENCE and ACTUAL are capture directories (values.jsonl plus <key>.rgba or <key>.rgba.gz).
Numbers inside values compare with the camera MSAA probe's float tolerance (absolute and
relative 2e-4); strings, booleans and log lists compare exactly. Image channels use the same
tolerance; NaN only matches NaN. Every missing, extra and failing value and image is kept in
the report and printed; the exit status is nonzero when anything fails.
"""
import argparse
import gzip
import json
import math
from pathlib import Path
import sys

import numpy as np

TOLERANCE = 2e-4


def values(root):
    rows = [json.loads(s) for s in (root/'values.jsonl').read_text().splitlines() if s.strip()]
    result = {r['k']: r['v'] for r in rows}
    if len(rows) != len(result) or not rows:
        raise SystemExit(f'{root}: empty capture or duplicate keys')
    return result


def equal(a, b):
    if isinstance(a, bool) or isinstance(b, bool):
        return a == b
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(equal(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(equal(a[k], b[k]) for k in a)
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return math.isfinite(a) and math.isfinite(b) and math.isclose(a, b, rel_tol=TOLERANCE, abs_tol=TOLERANCE)
    return a == b


def images(root):
    paths = list(root.glob('*.rgba')) + list(root.glob('*.rgba.gz'))
    result = {p.name.removesuffix('.gz').removesuffix('.rgba'): p for p in paths}
    if len(result) != len(paths):
        raise SystemExit(f'{root}: duplicate image keys')
    return result


def pixels(path):
    data = gzip.decompress(path.read_bytes()) if path.suffix == '.gz' else path.read_bytes()
    return np.frombuffer(data, dtype='<f4').reshape(-1, 4)


def compare_images(x, y):
    if x.shape != y.shape:
        return {'status': 'wrong-size', 'reference': list(x.shape), 'actual': list(y.shape)}
    with np.errstate(invalid='ignore'):
        delta = np.abs(x - y)
        ok = (x == y) | (np.isnan(x) & np.isnan(y)) | (
            np.isfinite(x) & np.isfinite(y) & (delta <= np.maximum(TOLERANCE, TOLERANCE*np.maximum(np.abs(x), np.abs(y)))))
    bad = ~ok.all(axis=1)
    row = {'status': 'pass' if ok.all() else 'different', 'channels': int(ok.size), 'matched': int(ok.sum()),
           'pixels': int(len(x)), 'failingPixels': int(bad.sum())}
    if bad.any():
        row['maxDelta'] = float(np.nanmax(np.where(ok, 0, np.nan_to_num(delta, nan=np.inf))))
        first = int(np.argmax(bad))
        row['first'] = {'pixel': first, 'reference': x[first].tolist(), 'actual': y[first].tolist()}
    return row


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    a, b = values(args.reference), values(args.actual)
    rows = []
    for key in sorted(a.keys() | b.keys()):
        status = 'extra' if key not in a else 'missing' if key not in b else 'pass' if equal(a[key], b[key]) else 'different'
        rows.append({'key': key, 'status': status, 'reference': a.get(key), 'actual': b.get(key)})
    ai, bi = images(args.reference), images(args.actual)
    expected = {k.removesuffix('.pixels') for k in a if k.endswith('.pixels')}
    image_rows = []
    for key in sorted(ai.keys() | bi.keys() | expected):
        if key not in ai:
            row = {'status': 'extra' if key in bi else 'missing-reference'}
        elif key not in bi:
            row = {'status': 'missing'}
        else:
            row = compare_images(pixels(ai[key]), pixels(bi[key]))
        row['key'] = key
        image_rows.append(row)
    summary = {
        'values': {s: sum(r['status'] == s for r in rows) for s in ('pass', 'different', 'missing', 'extra')},
        'images': {s: sum(r['status'] == s for r in image_rows) for s in sorted({r['status'] for r in image_rows} | {'pass'})},
        'channels': [sum(r.get('matched', 0) for r in image_rows), sum(r.get('channels', 0) for r in image_rows)],
        'tolerance': TOLERANCE,
    }
    if args.report:
        args.report.write_text(json.dumps({'summary': summary, 'values': rows, 'images': image_rows}, indent=1) + '\n')
    for r in rows:
        if r['status'] != 'pass':
            print(json.dumps(r, sort_keys=True))
    for r in image_rows:
        if r['status'] != 'pass':
            print(json.dumps(r, sort_keys=True))
    print(json.dumps(summary, sort_keys=True))
    return int(any(r['status'] != 'pass' for r in rows + image_rows))


if __name__ == '__main__':
    sys.exit(main())
