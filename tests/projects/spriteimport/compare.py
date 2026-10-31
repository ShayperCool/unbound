#!/usr/bin/env python3
"""Compares two sprite import captures (reference first), value by value, exactly.

    compare.py <reference sprites.jsonl> <candidate sprites.jsonl>

Numbers compare as 32-bit floats, without tolerance: the importer's mesh generation is exact.
Every missing, extra or differing value is listed; the exit status is 1 when any is.
"""
import argparse
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

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
            key = (r['texture'], r['name'])
            if key in records:
                raise ValueError(f'{path}: duplicate sprite {key}')
            records[key] = r
    return records


def compare(ref, cand):
    fields = []
    for key in sorted(ref.keys() | cand.keys()):
        for field in FIELDS:
            has_a = key in ref and field in ref[key]
            has_b = key in cand and field in cand[key]
            if key in ref and not has_a:
                raise ValueError(f'Incomplete reference: {key}/{field}')
            a = dict(flatten(ref[key][field])) if has_a else {}
            b = dict(flatten(cand[key][field])) if has_b else {}
            values = []
            for path in sorted(a.keys() | b.keys()):
                status = ('extra' if path not in a else 'missing' if path not in b
                          else 'pass' if a[path] == b[path] else 'different')
                values.append(dict(path=path, reference=a.get(path), actual=b.get(path), status=status))
            if not has_a:
                status = 'extra'
            elif not has_b:
                status = 'missing'
            else:
                status = 'pass' if all(v['status'] == 'pass' for v in values) else 'different'
            fields.append(dict(texture=key[0], name=key[1], field=field, status=status,
                               reference=ref[key].get(field) if has_a else None,
                               actual=cand[key].get(field) if has_b else None, values=values))
    return fields


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference')
    parser.add_argument('actual')
    parser.add_argument('--report', type=Path, help='Complete field/value union under the run cache')
    args = parser.parse_args()
    ref, cand = load(args.reference), load(args.actual)
    if not ref:
        parser.error('the reference is empty')
    fields = compare(ref, cand)
    matched = sum(f['status'] == 'pass' for f in fields)
    summary = dict(matched=matched, total=len(fields), reference_sprites=len(ref), candidate_sprites=len(cand))
    if args.report:
        report = cache_artifact_path(args.report)
        report.parent.mkdir(parents=True, exist_ok=True)
        with report.open('x') as stream:
            json.dump(dict(summary=summary, fields=fields), stream, indent=2)
            stream.write('\n')
    for f in fields:
        if f['status'] == 'pass':
            continue
        diffs = '; '.join(f"{f['field']}{v['path']} {v['reference']} != {v['actual']} ({v['status']})"
                          for v in f['values'] if v['status'] != 'pass')
        print(f"{f['texture']}/{f['name']} {f['field']} ({f['status']}): {diffs}")
    print(f'{matched}/{len(fields)} fields match ({len(ref)} reference sprites, {len(cand)} candidate sprites)')
    return int(matched != len(fields))


if __name__ == '__main__':
    sys.exit(main())
