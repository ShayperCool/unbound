#!/usr/bin/env python3
"""Compare a Painter2D capture (run.py) with the Editor reference.

    python3 tests/projects/uitkpainter/verify.py --release 6000.0.71f1 --candidate <capture dir> [--reference <dir>]

Meshes (detached painters, SaveToVectorImage): vertex positions and circle data within 2e-4
(relative above 1),
tints, arc flags and indices exactly, sizes within 2e-4. Fills bounded by arcs or curves
(ORDER_FREE) are compared as the set of their anti-aliased vertices (position, circle, tint, uv
and flags; the faded edges' interior apexes left out) plus the area of their plain interior triangles: libtess2 breaks ties between
co-circular points by the last bits of their coordinates, so the interior triangulation, a faded
edge's apex, a strip's diagonal and the emission order may differ while every anti-aliased edge
and the covered area agree. Cases in OPEN are compared and printed; of them only the order of their distinct tints is counted. Every other line (painter state, API calls and their log messages) must
be equal. Images (the same cases drawn on a runtime panel at scale 1 and 1.25, including a cell
that mixes allocated meshes and a painter stroke, and the gradient cases at scale 1) print their mean and worst channel difference
and pixels over 8, and fail beyond the recorded residuals (IMAGE_LIMITS).
"""
import argparse
import json
import math
from pathlib import Path
import struct
import sys
import zlib

ROOT = Path(__file__).resolve().parent
TOLERANCE = 2e-4
AREA_TOLERANCE = 1e-2
# The recorded image residuals (docs/COMPATIBILITY_GAPS.md, Known insignificant residuals): mean,
# worst channel difference and pixels over 8 may not exceed them. Scale 1: one pixel of
# fill-arc-closed-nomove; scale 1.25: two pixels of fill-rounded-rect and the panel background's
# 9/255 pattern on the outermost row and column. The gradient image: anti-aliased edge pixels
# within 8/255, none over.
IMAGE_LIMITS = {'painter-1.png': (0.02, 11, 1), 'painter-1.25.png': (0.02, 95, 32), 'painter-gradient.png': (0.02, 8, 0)}
# Cases recorded in the reference whose mesh is known to differ: compared and printed with their
# difference on every run (each names its follow-up in docs/COMPATIBILITY_GAPS.md). The order of
# their distinct tints is checked; the geometry of a curve without length (a loop back to its
# start, a curve shorter than the path epsilon) is the open part.
OPEN = {
    'gradient-curve-loop': 'stroke geometry of a curve whose ends coincide',
    'gradient-curve-loop-at-origin': 'stroke geometry of a curve whose ends coincide',
    'gradient-curve-tiny-at-origin': 'stroke geometry of a curve shorter than the path epsilon',
}
ORDER_FREE = {
    'fill-circle', 'fill-pie', 'fill-pie-ccw', 'fill-pie-small', 'fill-rounded-rect', 'fill-bezier',
    'fill-quadratic', 'fill-bezier-inflection', 'fill-arc-closed-nomove', 'fill-two-contours-oddeven',
}


def load(path):
    meshes, other = {}, []
    for line in path.read_text().splitlines():
        if not line.strip():
            continue
        r = json.loads(line)
        if r['k'] == 'mesh':
            meshes[r['case']] = r
        else:
            other.append(r)
    return meshes, other


def f32(x):
    return struct.unpack('<f', struct.pack('<f', x))[0] if math.isfinite(x) else x


def close(a, b):
    """Within TOLERANCE, relative to the magnitude above 1 (circle coordinates grow without
    bound where a curve's radius approaches half the line width)."""
    a, b = f32(a), f32(b)
    if math.isnan(a) or math.isnan(b):
        return math.isnan(a) and math.isnan(b)
    if not (math.isfinite(a) and math.isfinite(b)):
        return a == b
    return abs(a - b) <= TOLERANCE * max(1.0, abs(a))


def shapes(r):
    """The anti-aliased vertices (position and circle; each once, the faded edges' apexes left
    out) and the area of the plain interior triangles."""
    v, idx = r['v'], r['i']
    points, area = [], 0.0
    for t in range(0, len(idx), 3):
        tri = [v[j] for j in idx[t:t + 3]]
        if all(x[10] == 0 for x in tri):
            (x0, y0), (x1, y1), (x2, y2) = [(x[0], x[1]) for x in tri]
            area += abs((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)) / 2
            continue
        for x in tri:
            if x[12] == 0 and x[13] == 0 and f32(x[14]) == f32(-9999.9):
                continue
            p = tuple(f32(c) for c in (x[0], x[1], x[12], x[13], x[14], x[15])) + tuple(x[2:12])
            if not any(same_point(p, q) for q in points):
                points.append(p)
    return points, area


def same_point(a, b):
    """Positions and circles within the tolerance; tint, uv and flags equal."""
    return all(close(x, y) for x, y in zip(a[:6], b[:6])) and a[6:] == b[6:]


def unmatched(reference, candidate):
    """Candidate points without an equal reference point."""
    return sum(1 for p in candidate if not any(same_point(p, q) for q in reference))

def compare_mesh(name, a, b):
    """Differences of candidate b from reference a, as short strings."""
    out = []
    for k in (0, 1):
        if not close(a['size'][k], b['size'][k]):
            out.append(f'size {b["size"]} vs {a["size"]}')
            break
    if a['saved'] != b['saved']:
        out.append(f'saved {b["saved"]} vs {a["saved"]}')
    if name in ORDER_FREE:
        pa, aa = shapes(a)
        pb, ab = shapes(b)
        extra, missing = unmatched(pa, pb), unmatched(pb, pa)
        if extra or missing:
            out.append(f'anti-aliased vertices: {extra} of {len(pb)} not in the reference, {missing} of {len(pa)} missing')
        if abs(aa - ab) > AREA_TOLERANCE:
            out.append(f'interior area {ab:.3f} vs {aa:.3f}')
        return out
    if len(a['v']) != len(b['v']):
        out.append(f'{len(b["v"])} vertices vs {len(a["v"])}')
    if a['i'] != b['i']:
        out.append('indices differ')
    worst, where = 0.0, None
    for i, (x, y) in enumerate(zip(a['v'], b['v'])):
        for c in (0, 1, 12, 13, 14, 15):
            if not close(x[c], y[c]):
                d = abs(f32(x[c]) - f32(y[c])) if math.isfinite(x[c]) and math.isfinite(y[c]) else math.inf
                if d > worst:
                    worst, where = d, (i, c, y[c], x[c])
        if x[2:12] != y[2:12]:
            out.append(f'vertex {i} tint/uv/flags {y[2:12]} vs {x[2:12]}')
            break
    if where:
        out.append(f'vertex {where[0]} component {where[1]}: {where[2]} vs {where[3]} (worst {worst:.3g})')
    return out


def tints(r):
    """The distinct vertex tints in the order they first appear."""
    out = []
    for v in r['v']:
        t = tuple(v[2:6])
        if t not in out:
            out.append(t)
    return out


def read_png(path):
    data = path.read_bytes()
    pos, chunks = 8, []
    width = height = 0
    while pos < len(data):
        length, kind = struct.unpack('>I4s', data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == b'IHDR':
            width, height, depth, color = struct.unpack('>IIBB', body[:10])
            assert depth == 8 and color == 6, f'{path}: expected 8-bit RGBA'
        elif kind == b'IDAT':
            chunks.append(body)
        pos += 12 + length
    raw = zlib.decompress(b''.join(chunks))
    stride = width * 4
    rows, prev = [], bytearray(stride)
    for y in range(height):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for x in range(stride):
            a = line[x - 4] if x >= 4 else 0
            b = prev[x]
            c = prev[x - 4] if x >= 4 else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(bytes(line))
        prev = line
    return width, height, rows


def compare_image(a, b):
    wa, ha, ra = read_png(a)
    wb, hb, rb = read_png(b)
    if (wa, ha) != (wb, hb):
        return None
    total, worst, over = 0, 0, 0
    for row_a, row_b in zip(ra, rb):
        for x in range(0, len(row_a), 4):
            d = max(abs(row_a[x + k] - row_b[x + k]) for k in range(4))
            total += d
            worst = max(worst, d)
            over += d > 8
    return total / (wa * ha), worst, over


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--release', required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--reference', type=Path)
    a = p.parse_args()
    reference = a.reference or ROOT / 'Reference' / f'unity-{a.release}'
    ref_meshes, ref_other = load(reference / 'painter.jsonl')
    meshes, other = load(a.candidate / 'painter.jsonl')
    failures = 0
    for name in sorted(ref_meshes.keys() | meshes.keys()):
        if name not in meshes or name not in ref_meshes:
            print(f'{name}: {"missing" if name not in meshes else "extra"}')
            failures += 1
            continue
        diff = compare_mesh(name, ref_meshes[name], meshes[name])
        if name in OPEN:
            print(f'open: {name} ({OPEN[name]}): ' + ('; '.join(diff) if diff else 'matches'))
            # Counted for an open case: the colours along the stroke, in order. A case that
            # matches altogether no longer belongs in OPEN.
            if tints(ref_meshes[name]) != tints(meshes[name]):
                failures += 1
                print(f'{name}: tints in order {tints(meshes[name])} vs {tints(ref_meshes[name])}')
            elif not diff:
                failures += 1
                print(f'{name}: matches the reference; remove it from OPEN')
            continue
        if diff:
            failures += 1
            print(f'{name}: ' + '; '.join(diff))
    for i in range(max(len(ref_other), len(other))):
        x = ref_other[i] if i < len(ref_other) else None
        y = other[i] if i < len(other) else None
        if x != y:
            failures += 1
            print(f'line {i}: {json.dumps(y)} vs {json.dumps(x)}')
    checks = len(ref_meshes.keys() | meshes.keys()) + max(len(ref_other), len(other)) + len(IMAGE_LIMITS)
    for image in IMAGE_LIMITS:
        r, c = reference / image, a.candidate / image
        if not r.is_file() or not c.is_file():
            print(f'{image}: missing')
            failures += 1
            continue
        result = compare_image(r, c)
        if result is None:
            print(f'{image}: size differs')
            failures += 1
            continue
        mean, worst, over = result
        print(f'{image}: mean {mean:.2f}, worst {worst}, over 8: {over}')
        limit = IMAGE_LIMITS[image]
        if mean > limit[0] or worst > limit[1] or over > limit[2]:
            print(f'{image}: beyond the recorded residuals (mean {limit[0]}, worst {limit[1]}, over 8: {limit[2]})')
            failures += 1
    print(f'{checks - failures}/{checks} painter checks match')
    sys.exit(1 if failures else 0)


if __name__ == '__main__':
    main()
