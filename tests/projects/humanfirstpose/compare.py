#!/usr/bin/env python3
"""Write the complete field union, raw f32 differences, and the unchanged 2e-4
absolute/relative numeric tolerance result. No quaternion sign or input normalization.
"""
import argparse
import json
import math
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

TOLERANCE = 2e-4


def float_bits(value):
    if isinstance(value, str):
        value = float(value)
    return '0x' + struct.pack('>f', value).hex()


def atom(value):
    return isinstance(value, dict) and set(value) == {'f', 'bits'}


def leaves(value, path=''):
    if atom(value):
        yield path, value
    elif isinstance(value, dict):
        for key, child in value.items():
            yield from leaves(child, path + '.' + key)
    elif isinstance(value, list):
        for i, child in enumerate(value):
            yield from leaves(child, path + '[' + str(i) + ']')
    else:
        yield path, value


def float_leaves(value, path=''):
    for name, child in leaves(value, path):
        if atom(child) or isinstance(child, (int, float)) and not isinstance(child, bool):
            yield name, child


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    values = {r['k']: r['v'] for r in rows}
    if not rows or len(rows) != len(values):
        raise ValueError(f'{path}: empty capture or duplicate keys')
    return values


def validate(values):
    if values.get('complete') != {'curves': 7, 'callbacks': 1, 'updates': 2, 'elapsedTime': 0}:
        raise ValueError('missing or invalid completion record')
    if 'exception' in values:
        raise ValueError(values['exception'])
    curves = {'curve.Root' + kind + '.' + axis for kind, axes in [('T', 'xyz'), ('Q', 'xyzw')] for axis in axes}
    if {k for k in values if k.startswith('curve.')} != curves:
        raise ValueError('incomplete RootT/RootQ curve set')
    for name in curves:
        curve = values[name]
        if len(curve['keys']) != 31 or curve['keys'][0]['time']['f'] != 0 or curve['keys'][-1]['time']['f'] != 1:
            raise ValueError(f'{name}: expected the complete 31-key one-second Scene curve')
        if curve['keys'][0]['value']['bits'] != curve['evaluate0']['bits']:
            raise ValueError(f'{name}: Evaluate(0) differs from the first key')
    if values['clip']['name'] != 'Scene' or not values['clip']['humanMotion']:
        raise ValueError('incorrect human clip')
    for case in ('playA', 'ik'):
        for stage in ('before', 'after'):
            snapshot = values[case + '.' + stage]
            required = {'position', 'localPosition', 'rotation', 'localRotation', 'localScale', 'lossyScale',
                        'localToWorldMatrix', 'humanScale', 'bodyPosition', 'bodyRotation', 'rootPosition', 'rootRotation', 'hasParent'}
            if not required <= snapshot.keys():
                raise ValueError(f'{case}.{stage}: incomplete public transform/animator capture')
            if snapshot['hasParent'] or snapshot['humanScale']['f'] <= 1 or snapshot['localScale'][0]['f'] != 100:
                raise ValueError(f'{case}.{stage}: wrong rig setup')
            if len(snapshot['localToWorldMatrix']) != 4 or any(len(row) != 4 for row in snapshot['localToWorldMatrix']):
                raise ValueError('incomplete matrix')
        active = values[case + '.after.active']
        if active['clip'] != 'Scene' or active['weight']['f'] <= 0 or active['normalizedTime']['f'] != 0 or not active['enabled']:
            raise ValueError(f'{case}: clip not playing at zero time')
        if abs(values[case + '.after']['bodyPosition'][2]['f']) < 100:
            raise ValueError(f'{case}: vacuous first body position')
    if values['ik.callback.0']['layer'] != 0 or abs(values['ik.callback.0']['bodyPosition'][2]['f']) < 100:
        raise ValueError('missing or vacuous IK callback')
    for key, value in values.items():
        for field, child in leaves(value):
            # Mono's R formatter prints both signed zeros as "0". Keep the bits
            # authoritative and compare their signs; never rewrite the input.
            signed_zero = atom(child) and child['f'] == 0 and child['bits'] in ('0x00000000', '0x80000000')
            if atom(child) and not signed_zero and float_bits(child['f']) != child['bits']:
                raise ValueError(f'{key}{field}: decimal/bit disagreement: {child}')


def compare(reference, actual):
    a = {key + path: value for key, value in reference.items() for path, value in leaves(value)}
    b = {key + path: value for key, value in actual.items() for path, value in leaves(value)}
    union = []
    for field in sorted(a.keys() | b.keys()):
        row = {'field': field}
        if field not in a or field not in b:
            row.update(status='extra' if field not in a else 'missing', reference=a.get(field), actual=b.get(field), exact=False, within_tolerance=False)
        else:
            x, y = a[field], b[field]
            if atom(x) and atom(y):
                exact = x['bits'] == y['bits']
                xf, yf = float(x['f']), float(y['f'])
                tolerance = math.isclose(xf, yf, rel_tol=TOLERANCE, abs_tol=TOLERANCE)
                row.update(status='equal' if exact else 'bits', reference=x, actual=y, exact=exact, within_tolerance=tolerance,
                           absolute_error=abs(xf-yf) if math.isfinite(xf) and math.isfinite(yf) else None)
            else:
                row.update(status='equal' if x == y else 'value', reference=x, actual=y, exact=x == y, within_tolerance=x == y)
        union.append(row)
    differences = [r for r in union if not r['exact']]
    return {'tolerance': {'absolute': TOLERANCE, 'relative': TOLERANCE}, 'fields': len(union),
            'differences': differences, 'tolerance_failures': [r for r in union if not r['within_tolerance']], 'union': union}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('--json', required=True, type=Path)
    args = parser.parse_args()
    cache_artifact_path(args.json)
    # Persist every missing/extra/differing field even when structural validation fails.
    a, b = load(args.reference), load(args.actual)
    report = compare(a, b)
    errors = []
    for name, values in [('reference', a), ('actual', b)]:
        try:
            validate(values)
        except (ValueError, KeyError, TypeError) as e:
            errors.append({'capture': name, 'error': str(e)})
    report['validation_errors'] = errors
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    print(json.dumps({k: len(v) if isinstance(v, list) else v for k, v in report.items() if k != 'union'}))
    return int(bool(report['differences'] or errors))


if __name__ == '__main__':
    raise SystemExit(main())
