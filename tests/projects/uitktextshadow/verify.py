#!/usr/bin/env python3
"""Compare a text shadow capture (run.py) with the Editor reference.

    python3 tests/projects/uitktextshadow/verify.py --release 6000.0.71f1 --candidate <capture dir> [--reference <dir>]

shadow.jsonl (each label's resolved outline and layout) must be equal, but for the lines in KNOWN,
which are printed with their difference on every run (and fail once they match). In each image (panel scale
1 and 1.25) every case's cell is compared inside its edge (2 pixels in): exactly. The whole image
prints its mean and worst channel difference and pixels over 8, and fails beyond the recorded
residuals (IMAGE_LIMITS): the cells' outer corner pixels, where the Editor's anti-aliased
rectangle edge leaves a faint pixel diagonally outside each corner (12 cells, 4 corners).
"""
import argparse
import json
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import device_reference_root  # noqa: E402
from value_probe import read_png  # noqa: E402

# Assets/Scripts/TextShadowProbe.cs: the cases in order, three per row in cells of 200 x 96 points,
# drawn 4 points in as 192 x 88 rectangles.
CASES = ['plain', 'outline-no-width', 'shadow', 'shadow-blur', 'shadow-negative', 'shadow-far',
         'shadow-wide-blur', 'outline', 'outline-shadow', 'face-transparent', 'small', 'bold']
COLUMNS, CELL_W, CELL_H, MARGIN, INSET = 3, 200, 96, 4, 2
IMAGES = {'shadow-1.png': 1.0, 'shadow-1.25.png': 1.25}
# Lines of shadow.jsonl known to differ, with the reason. The bold label is laid out narrower: its
# text measures 186.4 wide in Unbound, 193.6 in the Editor; its glyphs are drawn at the same places
# (its cell matches).
KNOWN = {'bold': 'bold text of the default font measures narrower than the Editor\'s (layout width 186.4 vs 193.6)'}
IMAGE_LIMITS = {'shadow-1.png': (0.02, 24, 48), 'shadow-1.25.png': (0.04, 24, 48)}


def differences(a, b):
    wa, ha, ra = read_png(a)
    wb, hb, rb = read_png(b)
    if (wa, ha) != (wb, hb):
        return None
    return [[max(abs(row_a[x + k] - row_b[x + k]) for k in range(4)) for x in range(0, len(row_a), 4)]
            for row_a, row_b in zip(ra, rb)]


def cell(index, scale):
    x = MARGIN + (index % COLUMNS) * CELL_W
    y = MARGIN + (index // COLUMNS) * CELL_H
    return (round(x * scale) + INSET, round(y * scale) + INSET,
            round((x + CELL_W - 2 * MARGIN) * scale) - INSET, round((y + CELL_H - 2 * MARGIN) * scale) - INSET)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--release', required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--reference', type=Path)
    a = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
    reference = a.reference or device_reference_root(ROOT)
    checks = failures = 0
    expected = (reference / 'shadow.jsonl').read_text().splitlines()
    actual = (a.candidate / 'shadow.jsonl').read_text().splitlines() if (a.candidate / 'shadow.jsonl').is_file() else []
    for i in range(max(len(expected), len(actual))):
        checks += 1
        x = json.loads(expected[i]) if i < len(expected) else None
        y = json.loads(actual[i]) if i < len(actual) else None
        known = KNOWN.get((x or {}).get('case'))
        if known:
            print(f'known: line {i} ({known}): ' + ('matches; remove it from KNOWN' if x == y else f'{json.dumps(y)} vs {json.dumps(x)}'))
            failures += x == y
        elif x != y:
            failures += 1
            print(f'line {i}: {json.dumps(y)} vs {json.dumps(x)}')
    for image, scale in IMAGES.items():
        r, c = reference / image, a.candidate / image
        checks += 1 + len(CASES)
        if not r.is_file() or not c.is_file():
            print(f'{image}: missing')
            failures += 1 + len(CASES)
            continue
        d = differences(r, c)
        if d is None:
            print(f'{image}: size differs')
            failures += 1 + len(CASES)
            continue
        for index, name in enumerate(CASES):
            x0, y0, x1, y1 = cell(index, scale)
            worst = max(max(row[x0:x1]) for row in d[y0:y1])
            if worst:
                failures += 1
                over = sum(v > 8 for row in d[y0:y1] for v in row[x0:x1])
                print(f'{image} {name}: worst {worst}, over 8: {over}')
        values = [v for row in d for v in row]
        mean, worst, over = sum(values) / len(values), max(values), sum(v > 8 for v in values)
        print(f'{image}: mean {mean:.3f}, worst {worst}, over 8: {over}')
        limit = IMAGE_LIMITS[image]
        if mean > limit[0] or worst > limit[1] or over > limit[2]:
            print(f'{image}: beyond the recorded residuals (mean {limit[0]}, worst {limit[1]}, over 8: {limit[2]})')
            failures += 1
    print(f'{checks - failures}/{checks} text shadow checks match')
    sys.exit(1 if failures else 0)


if __name__ == '__main__':
    main()
