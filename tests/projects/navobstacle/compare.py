#!/usr/bin/env python3
"""Compares a capture of the NavMeshObstacle probe with the Editor's (see README.md).

    compare.py REFERENCE CANDIDATE [--lane PREFIX] [--details N] [--report PATH]

A record is one agent of one lane in one frame (or a lane's event). Every value of the
reference must be present in the candidate and match: positions, rotations, corners, link
ends and distances within 1e-3, velocities within 2e-3 (the NavMesh probes' tolerances),
everything else exactly. Every missing, extra or failing value is counted and, with
--report, written out in full; the first --details of them (default 20) are printed.
"""
import json
import math
import sys

VELOCITY = ('v', 'dv')
POSITION_TOL = 1e-3
VELOCITY_TOL = 2e-3


def load(path):
    out = {}
    for line in open(path):
        if line.strip():
            r = json.loads(line)
            key = (r.pop('lane'), r.pop('agent'), r.pop('f'))
            # Several events of a lane in one frame are told apart by their order.
            while key in out:
                key = key + ('+',)
            out[key] = r
    return out


def number(x):
    if isinstance(x, str) and x in ('Infinity', '-Infinity', 'NaN'):
        return float(x.replace('Infinity', 'inf').replace('NaN', 'nan'))
    return x


def diff(a, b, tol, path, out):
    """Appends (path, reference, candidate) for every value of a that b does not match."""
    a, b = number(a), number(b)
    if isinstance(a, dict):
        if not isinstance(b, dict):
            out.append((path, a, b))
            return
        for k in a:
            diff(a[k], b.get(k, '<missing>'), tol, path + '.' + k, out)
        for k in b:
            if k not in a:
                out.append((path + '.' + k, '<missing>', b[k]))
        return
    if isinstance(a, list):
        if not isinstance(b, list) or len(a) != len(b):
            out.append((path, a, b))
            return
        for i, (x, y) in enumerate(zip(a, b)):
            diff(x, y, tol, f'{path}[{i}]', out)
        return
    if isinstance(a, float) or (isinstance(a, int) and not isinstance(a, bool)):
        if isinstance(b, bool) or not isinstance(b, (int, float)):
            out.append((path, a, b))
        elif math.isinf(a) or math.isinf(b) or math.isnan(a) or math.isnan(b):
            if not (a == b or (math.isnan(a) and math.isnan(b))):
                out.append((path, a, b))
        elif abs(a - b) > tol:
            out.append((path, a, b))
        return
    if a != b:
        out.append((path, a, b))


def count(v):
    if isinstance(v, dict):
        return sum(count(x) for x in v.values())
    if isinstance(v, list):
        return sum(count(x) for x in v) or 1
    return 1


def main():
    args = sys.argv[1:]
    opts = {'--lane': '', '--details': '20', '--report': None}
    pos = []
    i = 0
    while i < len(args):
        if args[i] in opts:
            opts[args[i]] = args[i + 1]
            i += 2
        else:
            pos.append(args[i])
            i += 1
    if len(pos) != 2:
        sys.exit(__doc__)
    ref, cand = load(pos[0]), load(pos[1])
    prefix = opts['--lane']
    failures = []
    lanes = {}
    values = matched = 0
    for key in sorted(ref, key=lambda k: (k[0], k[1], k[2], len(k))):
        if not key[0].startswith(prefix):
            continue
        r = ref[key]
        stats = lanes.setdefault(key[0], [0, 0, 0, 0])
        n = count(r)
        values += n
        stats[2] += n
        stats[1] += 1
        if key not in cand:
            failures.append((key, '<record>', 'present', '<missing>'))
            continue
        out = []
        for field, a in r.items():
            diff(a, cand[key].get(field, '<missing>'), VELOCITY_TOL if field in VELOCITY else POSITION_TOL, field, out)
        for field in cand[key]:
            if field not in r:
                out.append((field, '<missing>', cand[key][field]))
        bad = len(out)
        matched += max(0, n - bad)
        stats[3] += max(0, n - bad)
        if not out:
            stats[0] += 1
        failures += [(key,) + o for o in out]
    for key in cand:
        if key[0].startswith(prefix) and key not in ref:
            failures.append((key, '<record>', '<missing>', 'present'))
    for lane, (ok, total, nv, okv) in sorted(lanes.items()):
        print(f'{lane:16} records {ok}/{total}  values {okv}/{nv}')
    nrec = sum(s[1] for s in lanes.values())
    okrec = sum(s[0] for s in lanes.values())
    print(f'total: records {okrec}/{nrec}, values {matched}/{values}, {len(failures)} failing/missing/extra value(s)')
    for f in failures[:int(opts['--details'])]:
        print('  ', f)
    if opts['--report']:
        with open(opts['--report'], 'w') as out:
            for f in failures:
                out.write(json.dumps({'key': list(f[0]), 'value': f[1], 'reference': f[2], 'candidate': f[3]}) + '\n')
    sys.exit(1 if failures else 0)


if __name__ == '__main__':
    main()
