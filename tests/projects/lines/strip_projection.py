"""Offline float32 replays of the prepared strip rules on saved meshes.

Diagnostics only: each replay starts from the engine's saved mesh (or the authored
inputs), never from a Native value, and first proves that the unchanged rule
reproduces the saved engine mesh. It builds and runs nothing.

* strip_pairs: the corner arc and its inner side follow the un-eased outgoing side, a
  TransformZ trail's opposite face swaps that side, and the arc's position angle uses
  the normalized cosine. An empty rule set is the measured engine source, checked first.
* per-segment parameter: DistributePerSegment / RepeatPerSegment evaluate width and
  gradient at index / (n - 1) instead of distance / total.

project_rows applies both to every saved control they reach, after proving that the
unchanged rules reproduce each saved engine mesh (positions, triangles and colors).
"""
import math
import numpy as np
import tangent_projection as model

F, dot, mag, norm, cross = model.F, model.dot, model.mag, model.norm, model.cross


def _cap_triangles(caps):
    return caps + 1 if caps else 0


def strip_pairs(points, half, corners, transform_z, rules, loop=False, campos=(0, 0, -10), forward=(0, 0, 1)):
    """Float32 pairs and joins of LineStrip.Build through points with per-point half widths.

    rules selects the corner arithmetic. An empty set is the measured engine source (eased
    arc side and turn, raw Acos angle, no TransformZ trail corner swap). The prepared
    source is {'arc', 'reverse', 'normalized'}: 'arc' uses the un-eased outgoing side for
    the arc and its turn, 'reverse' swaps the corner side of a TransformZ trail, and
    'normalized' divides the position cosine by the side lengths. Returns [(A, B, join)]
    with join None for a quad, 'A' for CornerA (b0,b1,a1), 'B' for CornerB (a0,b1,a1).
    """
    pts = np.array(points, dtype='f')
    campos, forward = np.array(campos, dtype='f'), np.array(forward, dtype='f')
    count = len(pts)
    n = count + 1 if loop else count
    segments = n - 1
    half = [F(h) for h in half]
    at = lambda i: pts[i % count]
    ds = [at(j + 1) - at(j) for j in range(segments)]
    lens = [mag(d) for d in ds]
    zero = [dot(d, d) < F(1e-5) for d in ds]
    if all(zero):
        dirs = [d / l if l > 0 else np.array([-1, 0, 0], dtype='f') for d, l in zip(ds, lens)]
        zero = [False] * segments
    else:
        dirs = [np.zeros(3, dtype='f') if z else d / l for d, l, z in zip(ds, lens, zero)]
    nomiter = [False] * n
    for j in range(segments):
        if zero[j]:
            k = (j + 2) % segments if loop else j + 2
            if k < n:
                nomiter[k] = True
            if loop and k == 0:
                nomiter[n - 1] = True
    if not loop:
        lead = 0
        while lead < segments and zero[lead]:
            lead += 1
        if lead:
            nomiter[lead] = True
    widest = F(max(F(2) * abs(h) for h in half))
    eff = _effective(dirs, lens, zero, widest, loop)
    face = np.array([0, 0, -1], dtype='f') if transform_z else -forward

    def side(d, p):
        s = cross(d, face if transform_z else campos - p)
        if not transform_z:
            s = s - forward * dot(s, forward)
        return norm(s)

    pairs = []
    for i in range(n):
        p, h = at(i), half[i]
        has_in, has_out = i > 0 or loop, i < segments or loop
        din = eff[i - 1 if i > 0 else segments - 1] if has_in else np.zeros(3, dtype='f')
        dout = eff[i if i < segments else 0] if has_out else np.zeros(3, dtype='f')
        raw = dout
        if has_in and has_out and not loop:
            c = dot(din, dout)
            if c > F(0.99):
                wgt = F(F(F(1) - c) / F(F(1) - F(0.99)))
                e = din * F(F(1) - wgt) + dout * wgt
                dout = norm(e) if dot(e, e) > 0 else dout
        if not has_in or not has_out:
            s = side(dout if has_out else din, p)
            pairs.append((p - s * h, p + s * h, None))
            continue
        normal = face if transform_z else campos - p
        s1, s2 = side(din, p), side(dout, p)
        total = s1 + s2
        if dot(total, total) < F(1e-12):
            m, length = s1, h
        else:
            m = norm(total)
            length = F(h / max(dot(m, s1), F(1) / F(3)))
        if nomiter[i]:
            length = h
        elif 0 < i < segments and abs(length) > abs(h):
            reach = F(F(0.75) * max(F(0) if zero[i - 1] else lens[i - 1], F(0) if zero[i] else lens[i]))
            if reach > 0:
                length = F(math.copysign(1, h) * max(abs(h), min(abs(length), reach)))
        if not corners:
            pairs.append((p - m * length, p + m * length, None))
            continue
        arc = side(raw, p) if 'arc' in rules else s2
        turn = dot(cross(s1, arc), normal)
        a_inner = bool(turn > 0 or (turn == 0 and dot(s1, arc) < 0))
        if h <= 0:
            a_inner = not a_inner
        if 'reverse' in rules and transform_z:
            a_inner = not a_inner
        w = F(F(2) * h)
        inner = p - m * length if a_inner else p + m * length
        cc = dot(s1, arc)
        if 'normalized' in rules:
            den = F(math.sqrt(F(dot(s1, s1) * dot(arc, arc))))
            cc_angle = F(cc / den) if den > 0 else cc
        else:
            cc_angle = cc
        angle = F(math.acos(float(np.clip(cc_angle, -1, 1))))
        perp = arc - s1 * cc
        perp = norm(perp) if dot(perp, perp) > F(1e-12) else din
        for k in range(corners if loop and i == 0 else 0, corners + 1):
            f = F(F(k) / F(corners))
            r = s1 * F(math.cos(angle * f)) + perp * F(math.sin(angle * f))
            join = None if k == 0 else 'A' if a_inner else 'B'
            pairs.append((inner, inner + r * w, join) if a_inner else (inner - r * w, inner, join))
    return pairs


def _effective(dirs, lens, zero, width, loop):
    """LineStrip.EffectiveDirections / LoopDirections."""
    segments = len(dirs)
    if loop:
        first = next((j for j in range(segments) if not zero[j]), segments)
        if first == segments:
            return list(dirs)
        eff = [None] * segments
        nxt, j = dirs[first], first
        for _ in range(segments):
            j = j - 1 if j > 0 else segments - 1
            if not zero[j]:
                nxt = dirs[j]
            eff[j] = nxt
        return eff

    def short(j):
        if j < 0 or j >= segments:
            return F(0)
        if zero[j]:
            return F(1)
        return F(np.clip(F(F(F(2) * width) / lens[j] - F(1)), 0, 1))

    def ahead(j):
        if j >= segments:
            return np.zeros(3, dtype='f')
        if not zero[j]:
            return dirs[j]
        if j + 1 < segments:
            return dirs[j + 1]
        return dirs[j - 1] if j > 0 else np.zeros(3, dtype='f')

    eff = []
    chain = np.zeros(3, dtype='f')
    for j in range(segments):
        if zero[j]:
            chain = eff[j - 1] if j > 0 else np.zeros(3, dtype='f')
        elif j == 0:
            chain = dirs[0]
        else:
            chain = norm(dirs[j] + chain * short(j))
        e = chain + ahead(j + 1) * short(j + 1)
        e = norm(e) if dot(e, e) > 0 else (eff[j - 1] if j > 0 else ahead(j + 1))
        eff.append(e)
    for j in range(segments - 1, -1, -1):
        if dot(eff[j], eff[j]) == 0 and j + 1 < segments:
            eff[j] = eff[j + 1]
    return eff


def splice(mesh, caps, pairs):
    """The saved mesh with its strip vertices and strip triangles replaced by pairs."""
    first = caps + 3 if caps else 0
    v = np.array(mesh['v'], dtype='f').reshape(-1, 3).copy()
    if mesh['vc'] != 2 * first + 2 * len(pairs):
        raise ValueError(f"{mesh['vc']} vertices for {len(pairs)} pairs and {caps} caps")
    for j, (a, b, _) in enumerate(pairs):
        v[first + 2 * j], v[first + 2 * j + 1] = a, b
    tri = list(mesh['idx'][0]['i'])
    t = 3 * _cap_triangles(caps)
    for j in range(1, len(pairs)):
        a0 = first + 2 * j - 2
        b0, a1, b1 = a0 + 1, a0 + 2, a0 + 3
        join = pairs[j][2]
        if join is None:
            tri[t:t + 6] = [a0, b0, a1, b0, b1, a1]
            t += 6
        else:
            tri[t:t + 3] = [b0, b1, a1] if join == 'A' else [a0, b1, a1]
            t += 3
    if t != len(tri) - 3 * _cap_triangles(caps):
        raise ValueError('strip triangle count differs from the saved mesh')
    return v, tri


def hermite(keys, t):
    """Flat-tangent keys, as authored by new Keyframe(time, value)."""
    t = F(t)
    if t <= keys[0][0]:
        return F(keys[0][1])
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            s = F((t - F(t0)) / F(F(t1) - F(t0)))
            h01 = F(s * s * F(F(3) - F(2) * s))
            return F(F(v0) + F(F(v1) - F(v0)) * h01)
    return F(keys[-1][1])


def _byte(x):
    return int(F(min(max(float(x), 0.0), 1.0)) * F(255) + F(0.5))


def _linear(byte):
    x = F(byte / 255)
    if x <= F(0.04045):
        return F(x / F(12.92))
    if x < 1:
        return F(math.pow(float(F(F(x + F(0.055)) / F(1.055))), 2.4))
    return F(1)


def vertex_color(rgba, linear):
    r, g, b, a = (_byte(x) for x in rgba)
    if linear:
        r, g, b = (_byte(_linear(x)) for x in (r, g, b))
    return [r / 255, g / 255, b / 255, a / 255]


# ---- the saved controls each rule applies to (authored inputs from Assets/Scripts/LineProbe.cs) ----

def _p(x, y, z=0.0):
    return [x, y, z]


SHAPES = {
    'straight': [_p(-1, 0), _p(0, 0), _p(1, 0)], 'L': [_p(-1, -.5), _p(1, -.5), _p(1, 1)],
    'Lneg': [_p(-1, .5), _p(1, .5), _p(1, -1)], 'acute': [_p(-1, 0), _p(1, 0), _p(-.5, .5)],
    'reverse': [_p(-1, 0), _p(1, 0), _p(-1, 0)], 'halfback': [_p(-1, 0), _p(1, 0), _p(0, 0)],
    'obtuse': [_p(-1, 0), _p(.5, 0), _p(1.5, .3)], 'repeat': [_p(-1, 0), _p(0, 0), _p(0, 0), _p(1, 1)],
    'repeatEnd': [_p(-1, 0), _p(1, 0), _p(1, 0)], 'repeatStart': [_p(-1, 0), _p(-1, 0), _p(1, 0)],
    'same': [_p(.5, .5)] * 3, 'two': [_p(-1, 0), _p(1, .5)], 'tiny': [_p(0, 0), _p(.0001, 0)],
    'zigzag': [_p(-2, -.5), _p(-1.4, .6), _p(-.2, -.4), _p(.3, .8), _p(1.8, .1)],
    'square': [_p(-1, -1), _p(1, -1), _p(1, 1), _p(-1, 1)],
    'closedSquare': [_p(-1, -1), _p(1, -1), _p(1, 1), _p(-1, 1), _p(-1, -1)],
    'spatial': [_p(-1, -.5, 0), _p(0, .5, 1), _p(1, -.3, -.5), _p(1.5, .8, .5)],
    'dupStartTurn': [_p(-1, 0), _p(-1, 0), _p(0, 0), _p(1, 1)], 'dupEndTurn': [_p(-1, 0), _p(0, 0), _p(1, 1), _p(1, 1)],
    'dupMidLong': [_p(-2, 0), _p(-1, 0), _p(0, 0), _p(0, 0), _p(1, 1), _p(2, 1)],
    'dupTriple': [_p(-1, 0), _p(0, 0), _p(0, 0), _p(0, 0), _p(1, 1)], 'dupUneven': [_p(-.5, 0), _p(0, 0), _p(0, 0), _p(2, 2)],
    'dupTwice': [_p(-2, 0), _p(-1, 0), _p(-1, 0), _p(0, .5), _p(0, .5), _p(1, 0)],
    'four': [_p(-2, 0), _p(-1, 0), _p(0, 0), _p(1, 1)], 'near1e-5': [_p(-1, 0), _p(0, 0), _p(.00001, 0), _p(1, 1)],
    'near1e-3': [_p(-1, 0), _p(0, 0), _p(.001, 0), _p(1, 1)], 'near1e-2': [_p(-1, 0), _p(0, 0), _p(.01, 0), _p(1, 1)],
    'sweepL': [_p(-1, 0), _p(0, 0), _p(0, 1)],
    'bend160': [_p(-1, 0), _p(0, 0), [math.cos(160 * math.pi / 180), math.sin(160 * math.pi / 180), 0]],
}
PERSP = ([1.5, 1.2, -5], norm(np.array([.3, .2, 0], dtype='f') - np.array([1.5, 1.2, -5], dtype='f')))


def line_cases():
    """World-space corner lines under the orthographic/perspective probe cameras.

    Transformed (tilted/local) lines are not replayed: their L corners turn by 90 degrees,
    where neither the un-eased arc (no eased joint) nor the normalized cosine (1e-7 rad)
    can move a component by the 1e-5 tolerance.
    """
    out = []
    for shape in ['straight', 'L', 'Lneg', 'acute', 'reverse', 'halfback', 'obtuse', 'repeat', 'repeatEnd',
                  'repeatStart', 'same', 'two', 'tiny', 'zigzag', 'spatial']:
        for c in (1, 3):
            for cap in (0, 3):
                out.append((f'line.geo.{shape}.c{c}.cap{cap}', shape, dict(corners=c, caps=cap)))
    for shape in ['L', 'square', 'closedSquare', 'two', 'zigzag', 'reverse']:
        for cap in (0, 2):
            out.append((f'line.loop.{shape}.c2.cap{cap}', shape, dict(corners=2, loop=True)))
    for mode in ['Stretch', 'Tile', 'DistributePerSegment', 'RepeatPerSegment', 'Static']:
        for shape in ['zigzag', 'repeat']:
            for scale in ['s1x1', 's2x0.5']:
                out.append((f'line.tex.{mode}.{shape}.{scale}.c2', shape, dict(corners=2, caps=2)))
        out.append((f'line.tex.{mode}.loop.square.c2', 'square', dict(corners=2, loop=True)))
    out += [
        ('line.width.bump.c2', 'zigzag', dict(corners=2, caps=2, mult=.7, curve=[(0, .2), (.5, 1), (1, .5)])),
        ('line.width.toZero', 'L', dict(corners=2, caps=2, curve=[(0, 1), (1, 0)])),
        ('line.width.negative', 'L', dict(corners=2, caps=2, mult=-.5)),
        ('line.width.zero', 'L', dict(corners=2, caps=2, mult=0)),
        ('line.color.startEnd', 'L', dict(corners=2)), ('line.color.loop', 'square', dict(corners=2, loop=True)),
        ('line.align.TransformZ.identity.L.c2', 'L', dict(corners=2, tz=True)),
        ('line.align.TransformZ.spatial.c2cap2', 'spatial', dict(corners=2, caps=2, tz=True)),
        ('line.light.View.L.c2cap2', 'L', dict(corners=2, caps=2)),
        ('line.light.loop.square.c2', 'square', dict(corners=2, loop=True)),
        ('line.light.reverse.c2', 'reverse', dict(corners=2)),
        ('line.shadowBias.0', 'L', dict(corners=2, caps=2)), ('line.shadowBias.2', 'L', dict(corners=2, caps=2)),
        ('line.miter.160.c2', 'bend160', dict(corners=2)), ('line.L.c3cap3.color', 'L', dict(corners=3, caps=3)),
        ('line.zigzag.c2cap2.uv.Stretch', 'zigzag', dict(corners=2, caps=2)),
        ('line.zigzag.c2cap2.uv.Tile', 'zigzag', dict(corners=2, caps=2)),
        ('line.align.View.persp.L.c2cap2', 'L', dict(corners=2, caps=2, cam=PERSP)),
        ('line.align.View.persp.spatial.c2cap2', 'spatial', dict(corners=2, caps=2, cam=PERSP)),
        ('line.light.persp.spatial.c2cap2', 'spatial', dict(corners=2, caps=2, cam=PERSP)),
        ('line.persp.spatial.c2cap2.color', 'spatial', dict(corners=2, caps=2, cam=PERSP)),
    ]
    for mode in ['Blend', 'Fixed', 'PerceptualBlend']:
        out.append((f'line.color.{mode}', 'zigzag', dict(corners=2, caps=2)))
    for shape in ['dupStartTurn', 'dupEndTurn', 'dupMidLong', 'dupTriple', 'dupUneven', 'dupTwice', 'four',
                  'near1e-5', 'near1e-3', 'near1e-2']:
        out.append((f'line.dup.{shape}.c2', shape, dict(corners=2)))
    for w in [0.2, 0.6, 0.8, 1, 1.2, 1.4, 1.6, 1.8, 2, 2.4, 3, 4]:
        out.append((f'line.sweep.L.c2.w{w:g}', 'sweepL', dict(corners=2, mult=w)))
    return out


TRAIL_CORNERS = {'round': (2, 2, False), 'transformZ': (1, 1, True), 'apiLong': (2, 2, False)}
PREPARED = {'arc', 'reverse', 'normalized'}
# The opposite face belongs to TransformZ trails only (LineDrawing.Facing).
LINE_PREPARED = PREPARED - {'reverse'}


def _saved(values):
    """Float32 values as the probe writes them (nine significant digits)."""
    return [float(f'{float(x):.9g}') for x in np.asarray(values, dtype='f').reshape(-1)]


def _trail_half(points, index=False):
    """NewTrail's width: 0.3 times a flat-tangent curve from 1 to 0.25."""
    return [F(F(0.15) * hermite([(0, 1), (1, .25)], t)) for t in _params(points, index)]


def _params(points, index):
    points = np.array(points, dtype='f')
    n = len(points)
    if index:
        return [F(F(i) / F(n - 1)) for i in range(n)]
    dist = [F(0)]
    for d in np.diff(points, axis=0):
        dist.append(F(dist[-1] + mag(d)))
    return [F(x / dist[-1]) if dist[-1] > 0 else F(0) for x in dist]


def _red(t):
    """NewTrail's gradient: white to red, alpha 1 to 0.2."""
    t = F(t)
    g = F(F(1) + F(F(0) - F(1)) * t)
    return F(1), g, g, F(F(1) + F(F(0.2) - F(1)) * t)


def _bounds(v):
    low, high = v.min(axis=0), v.max(axis=0)
    return [((low + high) * F(.5)).tolist(), ((high - low) * F(.5)).tolist()]


def _errors(a, b, ia=None, ib=None):
    a, b = np.asarray(a, dtype=float).reshape(-1), np.asarray(b, dtype=float).reshape(-1)
    if a.shape != b.shape:
        return ['shape']
    bad = [f'.v[{j}]' for j in np.flatnonzero(np.abs(a - b) > 1e-5 + 1e-5 * np.abs(a))]
    if ia is not None:
        bad += [f'.idx[0].i[{j}]' for j, (x, y) in enumerate(zip(ia, ib)) if x != y]
    return bad


def project_rows(by):
    """Apply the prepared corner and per-segment rules to every applicable saved row's
    'prepared' value (already carrying the cap/tangent projection). Each replay must first
    reproduce the saved engine ('final') value with the unchanged rules; otherwise this
    raises. Returns one check record per replayed row."""
    checks = []

    def apply(key, family, final, pairs_old, pairs_new, caps, colors=None):
        prepared = by[key]['raw']['prepared']['value']
        bv, bt = splice(final, caps, pairs_old)
        baseline = _errors(final['v'], bv, final['idx'][0]['i'], bt)
        if baseline:
            raise ValueError(f'{key}: unchanged rules do not reproduce the saved mesh: {baseline[:6]}')
        nv, nt = splice(prepared, caps, pairs_new)
        new_v = _saved(nv)
        changed = new_v != _saved(prepared['v']) or nt != prepared['idx'][0]['i']
        if colors is not None:
            if [round(x * 255) for x in final['c']] != [round(x * 255) for c in colors[0] for x in c]:
                raise ValueError(f'{key}: unchanged rules do not reproduce the saved colors')
            new_c = _saved([x for c in colors[1] for x in c])
            if new_c != _saved(prepared['c']):
                changed = True
                prepared['c'] = new_c
        if changed:
            prepared['v'] = new_v
            prepared['idx'][0]['i'] = nt
            prepared['bounds'] = [_saved(x) for x in _bounds(nv)]
        checks.append(dict(key=key, family=family, baseline_errors=[], changed=bool(changed)))

    for key, row in by.items():
        parts = key.split('.')
        if not (key.startswith('trail.f') and key.endswith('.mesh') and row['raw']['prepared']['present']):
            continue
        final = row['raw']['final']['value']
        if not final or not final['vc']:
            continue
        name = '.'.join(parts[2:-2])
        if name in TRAIL_CORNERS:
            caps, corners, tz = TRAIL_CORNERS[name]
            v = np.array(final['v'], dtype='f').reshape(-1, 3)
            state = by[key[:-4] + 'state']['raw']['final']['value']
            p = np.array(state['p'], dtype='f').reshape(-1, 3)
            # Drawn points: the newest (start cap centre), the recorded points newest first
            # without the oldest, and the trimmed tail (end cap centre).
            points = np.array([v[0], *p[:0:-1], v[-(caps + 3)]], dtype='f')
            half = _trail_half(points)
            apply(key, 'trail-corner', final, strip_pairs(points, half, corners, tz, set()),
                  strip_pairs(points, half, corners, tz, PREPARED), caps)
        elif name in ('tex.DistributePerSegment', 'tex.RepeatPerSegment'):
            v = np.array(final['v'], dtype='f').reshape(-1, 2, 3)
            points = (v[:, 0] + v[:, 1]) * F(0.5)
            pairs, colors = [], []
            for index in (False, True):
                half = _trail_half(points, index)
                pairs.append(strip_pairs(points, half, 0, False, set()))
                colors.append([vertex_color(_red(t), True) for t in _params(points, index) for _ in range(2)])
            apply(key, 'trail-per-segment', final, pairs[0], pairs[1], 0, colors)
    for key, shape, o in line_cases():
        row = by.get(key)
        if row is None or not row['raw']['final']['present'] or not row['raw']['final']['value'].get('vc'):
            raise ValueError(f'{key}: missing saved line control')
        final = row['raw']['final']['value']
        pts = np.array(SHAPES[shape], dtype='f')
        loop = o.get('loop', False)
        n = len(pts) + (1 if loop else 0)
        drawn = np.array([pts[i % len(pts)] for i in range(n)], dtype='f')
        curve = o.get('curve')
        half = [F(F(F(0.5) * F(o.get('mult', .4))) * (hermite(curve, t) if curve else F(1))) for t in _params(drawn, False)]
        cam = o.get('cam', ([0, 0, -10], (0, 0, 1)))
        args = (o['corners'], o.get('tz', False))
        apply(key, 'line-corner', final, strip_pairs(pts, half, *args, set(), loop, cam[0], cam[1]),
              strip_pairs(pts, half, *args, LINE_PREPARED, loop, cam[0], cam[1]), o.get('caps', 0))
    for mode in ('DistributePerSegment', 'RepeatPerSegment'):
        key = f'line.tex.{mode}.zigzag.curve'
        final = by[key]['raw']['final']['value']
        pts = np.array(SHAPES['zigzag'], dtype='f')
        curve = [(0, .5), (1, 2)]
        pairs = []
        for index in (False, True):
            half = [F(F(0.2) * hermite(curve, t)) for t in _params(pts, index)]
            pairs.append(strip_pairs(pts, half, 0, False, set()))
        apply(key, 'line-per-segment', final, pairs[0], pairs[1], 0)
    return checks
