#!/usr/bin/env python3
"""Compare a collections capture with the published Editor reference.

    verify.py --release R --candidate DIR [--reference DIR]

Every line of collections.jsonl is keyed by its kind, section and step. A step matches when its
callback/event log is equal and its state (rows with their element, text, layout and selection,
scroll values, items order, element trees) is equal, numbers within 1e-3 (infinite and NaN
layout values match themselves). Sections listed in OPEN
are reported but do not fail the run (each names its follow-up); every other section must match
line for line, with no missing or extra lines. Images are measured and printed.
"""
import argparse
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
TOLERANCE = 1e-3
# Sections not implemented yet; reported with their counts, not gated.
OPEN = {
    'animated': 'animated reordering (ListViewReorderMode.Animated)',
    'animated-steps': 'animated reordering (ListViewReorderMode.Animated)',
    'columns': 'MultiColumnListView',
    'columns-steps': 'MultiColumnListView',
    'column-tree': 'MultiColumnTreeView',
    'column-tree-steps': 'MultiColumnTreeView',
}


def load(path):
    lines = {}
    for line in path.read_text().splitlines():
        record = json.loads(line)
        lines[(record['k'], record.get('section'), record.get('step'))] = record
    return lines


def same(a, b):
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool):
        # Infinite and NaN layout values (a justify offset divided by zero items) match themselves.
        if a == b or (math.isnan(a) and math.isnan(b)):
            return True
        return abs(a - b) <= TOLERANCE
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    return a == b


def first_difference(a, b, path=''):
    if same(a, b):
        return None
    if isinstance(a, list) and isinstance(b, list):
        for i, (x, y) in enumerate(zip(a, b)):
            d = first_difference(x, y, f'{path}[{i}]')
            if d:
                return d
        return f'{path}: length {len(a)} vs {len(b)}'
    if isinstance(a, dict) and isinstance(b, dict):
        for k in a:
            if k not in b:
                return f'{path}.{k}: missing'
            d = first_difference(a[k], b[k], f'{path}.{k}')
            if d:
                return d
        return f'{path}: extra keys {sorted(set(b) - set(a))}'
    return f'{path}: {json.dumps(a)[:160]} vs {json.dumps(b)[:160]}'


def section_of(key):
    return key[1] if key[0] != 'section-error' else key[1]


def images(reference, candidate):
    try:
        from PIL import Image, ImageChops
    except ImportError:
        print('images: PIL not available, not measured')
        return
    for name in ['collections-built.png', 'collections-final.png']:
        a, b = reference/name, candidate/name
        if not b.is_file():
            print(f'{name}: missing')
            continue
        x, y = Image.open(a).convert('RGB'), Image.open(b).convert('RGB')
        if x.size != y.size:
            print(f'{name}: size {x.size} vs {y.size}')
            continue
        diff = ImageChops.difference(x, y).convert('L')
        hist = diff.histogram()
        total = sum(hist)
        mean = sum(i * n for i, n in enumerate(hist)) / total
        worst = max(i for i, n in enumerate(hist) if n)
        over = sum(hist[9:])
        print(f'{name}: mean {mean:.3f} worst {worst} over8 {over}')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--reference', type=Path)
    a = p.parse_args()
    reference = a.reference or ROOT/'Reference'/f'unity-{a.release}'
    ref = load(reference/'collections.jsonl')
    cand = load(a.candidate/'collections.jsonl')
    gated_total = gated_ok = 0
    open_counts = {}
    failures = []
    for key, record in ref.items():
        section = key[1]
        open_reason = OPEN.get(section) or OPEN.get(f'{section}-steps')
        ok = key in cand and same(record, cand[key])
        if open_reason:
            done, total = open_counts.get(open_reason, (0, 0))
            open_counts[open_reason] = (done + ok, total + 1)
            continue
        gated_total += 1
        if ok:
            gated_ok += 1
        elif key not in cand:
            failures.append(f'missing {key}')
        else:
            failures.append(f'{key}: {first_difference(record, cand[key])}')
    for key, record in cand.items():
        if key in ref:
            continue
        section = key[1]
        if OPEN.get(section) or OPEN.get(f'{section}-steps'):
            continue
        gated_total += 1
        failures.append(f'extra {key}: {json.dumps(record)[:200]}')
    for failure in failures:
        print('FAIL', failure)
    for reason, (done, total) in sorted(open_counts.items()):
        print(f'open: {reason}: {done}/{total} lines match (follow-up, not gated)')
    images(reference, a.candidate)
    print(f'{gated_ok}/{gated_total} collection checks match')
    sys.exit(0 if not failures else 1)


if __name__ == '__main__':
    main()
