#!/usr/bin/env python3
"""Compare every record and every PNG. Field tolerance 1e-5, pixel 1/255, image max 2 and mean .25 bytes."""
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image

def compare(reference, candidate):
    reference, candidate = Path(reference), Path(candidate)
    a = [json.loads(x) for x in (reference / 'records.jsonl').read_text().splitlines()]
    b = [json.loads(x) for x in (candidate / 'records.jsonl').read_text().splitlines()]
    matched = 0
    failures = {}
    for i in range(max(len(a), len(b))):
        x, y = a[i] if i < len(a) else {}, b[i] if i < len(b) else {}
        tol = 1 / 255 + 1e-7 if ',' in x.get('key', '') else 1e-5
        ok = x.get('key') == y.get('key') and len(x.get('value', [])) == len(y.get('value', [])) and all(abs(u-v) <= tol for u,v in zip(x.get('value', []), y.get('value', [])))
        matched += ok
        if not ok:
            group = x.get('key', '<missing>').split('/')[0]
            failures[group] = failures.get(group, 0) + 1
            if ',' not in x.get('key', ''): print('FIELD', x, y)
    print(f'records: {matched}/{max(len(a),len(b))}')
    for key, count in failures.items(): print(f'record failures {key}: {count}')
    names_a, names_b = {p.name for p in reference.glob('*.png')}, {p.name for p in candidate.glob('*.png')}
    images = 0
    for name in sorted(names_a | names_b):
        if name not in names_a & names_b:
            print('MISSING', name); continue
        x, y = np.asarray(Image.open(reference/name)).astype(float), np.asarray(Image.open(candidate/name)).astype(float)
        d = abs(x-y); ok = d.max() <= 2 and d.mean() <= .25; images += ok
        print(f'{name}: mean={d.mean():.6f} max={d.max():g} {"PASS" if ok else "FAIL"}')
    print(f'images: {images}/{len(names_a|names_b)}')
    return matched == len(a) == len(b) and images == len(names_a|names_b)

if __name__ == '__main__': sys.exit(0 if compare(*sys.argv[1:]) else 1)
