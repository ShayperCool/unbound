#!/usr/bin/env python3
"""Offline sphere-trigger hypotheses using recorded Native f32 inputs, never case construction.

See SPHERE_MECHANISM.md for the static call path and instruction-derived variant.
This is a research comparator, not a runtime acceptance gate or a geometry API.
"""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))
from probe_paths import cache_artifact_path


def f(x):
    return struct.unpack('<f', struct.pack('<f', x))[0]


def bits(x):
    return struct.unpack('<I', struct.pack('<f', x))[0]


def add(a, b):
    return f(a + b)


def sub(a, b):
    return f(a - b)


def mul(a, b):
    return f(a * b)


def dot(a, b):
    # Native sweepSphereSphere dot instruction order: y, x, z.
    return add(add(mul(a[1], b[1]), mul(a[0], b[0])), mul(a[2], b[2]))


def vec(values):
    return [f(x) for x in values]


def tagged(values):
    if isinstance(values, list):
        return [tagged(x) for x in values]
    return {'value': f(values), 'bits': f'{bits(values):08x}'}


def minmax(c, e):
    return [sub(x, y) for x, y in zip(c, e)], [add(x, y) for x, y in zip(c, e)]


def intersect(amin, amax, bmin, bmax):
    return all(lo <= hi2 and lo2 <= hi for lo, hi, lo2, hi2 in zip(amin, amax, bmin, bmax))


def center_extents(a, ae, b, be):
    return all(abs(sub(x, y)) <= add(ex, ey) for x, y, ex, ey in zip(a, b, ae, be))


def encode(x):
    u = bits(x)
    return (~u & 0xffffffff) if u & 0x80000000 else u | 0x80000000


def integer_bounds(lo, hi):
    # BpBroadPhaseUpdate.h IntegerAABB, eGRID_SNAP_VAL=4. No tuning.
    return [((encode(x) >> 4) - 1) << 4 for x in lo], [(((encode(x) >> 4) + 1) << 4) | 4 for x in hi]


def sphere_sweep(start, end, center, cr, radius):
    """Boolean portion of the measured sphere-specialized PhysX sweep path."""
    delta = [sub(y, x) for x, y in zip(start, end)]
    # FindParticleOverlaps uses x*x + (y*y + z*z), each instruction rounded.
    length = f(math.sqrt(add(mul(delta[0], delta[0]), add(mul(delta[1], delta[1]), mul(delta[2], delta[2])))))
    direction = [f(x / length) for x in delta] if length > f(1e-15) else [0.0, 1.0, 0.0]
    motion = [mul(x, length) for x in direction]
    # GuSweepSphereSphere.cpp uses center1+motion followed by subtraction.
    vb = [sub(add(x, d), x) for x, d in zip(start, motion)]
    ab = [sub(x, y) for x, y in zip(start, center)]
    a = dot(vb, vb)
    b = mul(2.0, dot(vb, ab))
    total = add(cr, radius)
    c = sub(dot(ab, ab), mul(total, total))
    if c <= 0.0 or a == 0.0:
        hit, reason = True, 'initial_overlap' if c <= 0.0 else 'zero_motion_branch'
    else:
        q = sub(mul(b, b), mul(mul(4.0, a), c))
        if q < 0.0:
            hit, reason = False, 'complex_roots'
        else:
            sq = f(math.sqrt(q))
            divisor = f(1.0 / mul(2.0, a))
            roots = sorted([mul(add(-b, sq), divisor), mul(sub(-b, sq), divisor)])
            hit = not (roots[1] < 0.0 or roots[0] > 1.0)
            reason = 'sweep_roots' if hit else 'roots_outside_step'
    return hit, dict(length=tagged(length), motion=tagged(motion), a=tagged(a), b=tagged(b), c=tagged(c), reason=reason)


def predictions(particle, geometry, dt, radius_scale):
    p = vec(particle['position'])
    assert bits(p[2]) == int(particle['zBits'], 16)
    c = vec(geometry['boundsCenter'])
    e = [mul(x, .5) for x in geometry['boundsSize']]
    radius = mul(max(vec(particle['currentSize3D'])), mul(f(radius_scale), .5))
    assert bits(radius) == int(particle['query']['radiusBits'], 16)
    assert bits(radius) == bits(mul(mul(f(particle['currentSize']), .5), f(radius_scale)))
    r = [radius] * 3
    cmin, cmax = minmax(c, e)
    pmin, pmax = minmax(p, r)
    names = {}
    names['center_extents'] = center_extents(p, r, c, e)
    names['minmax_intersection'] = intersect(pmin, pmax, cmin, cmax)
    names['expanded_collider'] = all(sub(lo, radius) <= x <= add(hi, radius) for x, lo, hi in zip(p, cmin, cmax))
    pc = [mul(add(lo, hi), .5) for lo, hi in zip(pmin, pmax)]
    pe = [mul(sub(hi, lo), .5) for lo, hi in zip(pmin, pmax)]
    names['particle_recentered'] = center_extents(pc, pe, c, e)
    pc = [add(lo, ex) for lo, ex in zip(pmin, pe)]
    qmin, qmax = minmax(pc, pe)
    names['bounds_setminmax_roundtrip'] = intersect(qmin, qmax, cmin, cmax)
    closest = [max(lo, min(x, hi)) for x, lo, hi in zip(p, cmin, cmax)]
    delta = [sub(x, y) for x, y in zip(p, closest)]
    names['clamp_box_axes'] = all(abs(x) <= radius for x in delta)
    names['clamp_box_squared'] = dot(delta, delta) <= mul(radius, radius)
    names['physx_integer_bounds'] = intersect(*integer_bounds(pmin, pmax), *integer_bounds(cmin, cmax))
    ce = [add(x, f(geometry['contactOffset'])) for x in e]
    names['physx_contact_integer_bounds'] = intersect(*integer_bounds(pmin, pmax), *integer_bounds(*minmax(c, ce)))
    names['physx_pruner_bounds'] = intersect(*minmax(p, [mul(x, f(1.01)) for x in r]),
                                             *minmax(c, [mul(x, f(1.01)) for x in e]))
    # Derived from actual Native instructions before evaluating this variant.
    velocity = vec(particle['totalVelocity'])
    start = [sub(x, mul(v, f(dt))) for x, v in zip(p, velocity)]
    delta = [sub(y, x) for x, y in zip(start, p)]
    half = [mul(x, .5) for x in delta]
    sweep_center = [add(x, h) for x, h in zip(start, half)]
    sweep_extents = [add(abs(h), radius) for h in half]
    pad = struct.unpack('<f', struct.pack('<I', 0x3727c5ac))[0]
    padded = [add(x, pad) for x in e]
    aabb = center_extents(sweep_center, sweep_extents, c, padded)
    sweep, sweep_data = sphere_sweep(start, p, c, f(geometry['colliderRadius']), radius)
    names['native_sphere_sweep'] = aabb and sweep
    # Deliberate diagnostic projection, not asserted as the actual moving path.
    names['native_padded_endpoint_only'] = center_extents(p, r, c, padded)
    details = dict(endpoint=tagged(p), start=tagged(start), velocity=tagged(velocity), radius=tagged(radius),
                   collider_center=tagged(c), collider_extents=tagged(e), collider_min=tagged(cmin),
                   collider_max=tagged(cmax), particle_min=tagged(pmin), particle_max=tagged(pmax),
                   endpoint_gaps=tagged([sub(sub(x, radius), hi) for x, hi in zip(p, cmax)]),
                   center_gaps=tagged([sub(abs(sub(x, y)), add(ex, radius)) for x, y, ex in zip(p, c, e)]),
                   padded_extents=tagged(padded), sweep_center=tagged(sweep_center),
                   sweep_extents=tagged(sweep_extents), aabb_pass=aabb, narrowphase=sweep_data)
    return names, details


def load(path):
    rows = [json.loads(line) for line in path.read_text().splitlines()]
    result = {(row['scenario'], row['step']): row for row in rows}
    if not rows or len(rows) != len(result):
        raise ValueError('Empty recording or duplicate keys: ' + str(path))
    return result


def raw_differences(a, b, path=''):
    if isinstance(a, dict) and isinstance(b, dict):
        for k in sorted(a.keys() | b.keys()):
            if k not in a or k not in b:
                yield dict(path=path+'/'+k, expected=a.get(k, 'MISSING'), actual=b.get(k, 'MISSING'))
            else:
                yield from raw_differences(a[k], b[k], path+'/'+k)
    elif isinstance(a, list) and isinstance(b, list):
        for i in range(max(len(a), len(b))):
            if i >= len(a) or i >= len(b):
                yield dict(path=path+'/'+str(i), expected=a[i] if i < len(a) else 'MISSING', actual=b[i] if i < len(b) else 'MISSING')
            else:
                yield from raw_differences(a[i], b[i], path+'/'+str(i))
    elif a != b:
        yield dict(path=path, expected=a, actual=b)


def raw_union(a, b):
    return [dict(key=key, **d) for key in sorted(a.keys() | b.keys())
            for d in raw_differences(a.get(key, 'MISSING'), b.get(key, 'MISSING'))]


def analyze(rows):
    cases = []
    dt = rows['setup', 'synced']['value']['dt']
    for (key, phase), row in sorted(rows.items()):
        if phase != 'input' or not key.endswith('/killOutside'):
            continue
        twin = key.removesuffix('/killOutside') + '/ignoreOutside'
        before, after, ignore = [rows[k, phase]['value'] for k, phase in [(key, 'before'), (key, 'after'), (twin, 'after')]]
        assert before['count'] == before['read'] == ignore['count'] == ignore['read'] == 1
        assert after['count'] == after['read'] and after['count'] in (0, 1)
        assert before['particles'][0]['life'] > 0 and ignore['particles'][0]['life'] > 0
        if key.startswith('arithmetic/'):
            geometry = rows['/'.join(key.split('/')[:2]), 'geometry']['value']
        else:
            geometry = rows['setup', 'synced']['value']
        # Use observed post-movement Native endpoint, including moving-from histories.
        endpoint = ignore['particles'][0]
        predicted, details = predictions(endpoint, geometry, dt, row['value'].get('radiusScale', 1))
        inside = after['count'] == 1
        cases.append(dict(key=key, inside=inside, predictions=predicted, details=details,
                          geometry=geometry, input=row, before=rows[key, 'before'], after=rows[key, 'after'],
                          ignore_after=rows[twin, 'after']))
        if key.startswith('arithmetic/'):
            assert predicted['center_extents'] == row['value']['predicates']['expandedAabb']
    groups = {}
    for name in cases[0]['predictions']:
        groups[name] = {}
        for family, selected in [('all', cases), ('arithmetic', [x for x in cases if x['key'].startswith('arithmetic/')]),
                                 ('boundary', [x for x in cases if x['key'].startswith('boundary/')])]:
            bad = [x['key'] for x in selected if x['predictions'][name] != x['inside']]
            groups[name][family] = dict(cases=len(selected), matches=len(selected)-len(bad), counterexamples=bad)
    return dict(cases=cases, variants=groups)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--unity-version', required=True)
    p.add_argument('--recording', type=Path)
    p.add_argument('--repeat', type=Path, required=True)
    p.add_argument('--actual', type=Path)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    cache_artifact_path(args.output)
    recording = args.recording or HERE/'Reference'/('unity-'+args.unity_version+'.jsonl')
    rows, repeat = load(recording), load(args.repeat)
    result = dict(unity_version=args.unity_version, native=analyze(rows),
                  exact_native_pair=recording.read_bytes() == args.repeat.read_bytes(),
                  raw_native_nondeterminism_union=raw_union(rows, repeat))
    paths = [recording, args.repeat, Path(__file__)]
    if args.actual:
        actual = load(args.actual)
        result['actual'] = analyze(actual)
        result['raw_cross_runtime_union'] = raw_union(rows, actual)
        spec = importlib.util.spec_from_file_location('trigger_verify', HERE.parent/'particletrigger/verify.py')
        trigger = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(trigger)
        result['strict_cross_runtime_union'] = trigger.report(rows, actual)
        paths.extend([args.actual, HERE.parent/'particletrigger/verify.py', HERE.parent/'particles/compare.py'])
    result['sha256'] = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(dict(native_pair_exact=result['exact_native_pair'], variants=result['native']['variants']), indent=2))
    return int(not result['exact_native_pair'])


if __name__ == '__main__':
    sys.exit(main())
