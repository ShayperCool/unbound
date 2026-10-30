#!/usr/bin/env python3
"""Compare baked particle stream layout and values at the original 2e-4 tolerance."""
import json
import math
import sys
from pathlib import Path
from PIL import Image


def load(path):
    return {x['k']: x['v'] for x in map(json.loads, (Path(path) / 'values.jsonl').read_text().splitlines())}


def differences(a, b, path=''):
    if isinstance(a, dict) and isinstance(b, dict):
        for key in a.keys() | b.keys():
            if key not in a or key not in b:
                yield path + '.' + key, a.get(key, 'MISSING'), b.get(key, 'MISSING')
            else:
                yield from differences(a[key], b[key], path + '.' + key)
    elif isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        for i, (x, y) in enumerate(zip(a, b)):
            yield from differences(x, y, path + f'[{i}]')
    elif isinstance(a, (float, int)) and isinstance(b, (float, int)) and not isinstance(a, bool) and not isinstance(b, bool):
        if not math.isclose(a, b, rel_tol=2e-4, abs_tol=2e-4):
            yield path, a, b
    elif a != b:
        yield path, a, b


if __name__ == '__main__':
    reference, actual = map(load, sys.argv[1:3])
    failed = 0
    for key, expected in reference.items():
        diff = list(differences(expected, actual.get(key, 'MISSING')))
        if diff:
            failed += 1
            if '-q' not in sys.argv:
                print(key, '; '.join(f'{p}: Unity {a}, Unbound {b}' for p, a, b in diff[:3]))
    extras = actual.keys() - reference.keys()
    print(f'particle streams: {len(reference)-failed}/{len(reference)} records match, {failed} differ, {len(extras)} unexpected')
    images = sorted(Path(sys.argv[1]).glob('*.png'))
    image_failed = 0
    for expected_path in images:
        actual_path = Path(sys.argv[2]) / expected_path.name
        if not actual_path.is_file():
            image_failed += 1
            print(f'{expected_path.name}: missing')
            continue
        with Image.open(expected_path) as expected, Image.open(actual_path) as candidate:
            matches = expected.size == candidate.size and expected.convert('RGBA').tobytes() == candidate.convert('RGBA').tobytes()
        image_failed += not matches
        if not matches:
            print(f'{expected_path.name}: pixels differ')
    print(f'particle streams: {len(images)-image_failed}/{len(images)} GPU images match byte for byte')
    sys.exit(int(failed > 0 or extras or image_failed or not reference))
