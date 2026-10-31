#!/usr/bin/env python3
"""Compare the complete oracle: keys, image manifest, and every float channel."""
import gzip
import json
import sys
from pathlib import Path
import numpy as np


def values(root):
    rows = [json.loads(s) for s in (root/'values.jsonl').read_text().splitlines()]
    result = {r['k']: r['v'] for r in rows}
    assert len(rows) == len(result), 'duplicate observation keys'
    return result


def equal(a, b):
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(equal(x, y) for x, y in zip(a, b))
    if isinstance(a, (int,float)) and isinstance(b, (int,float)):
        return bool(np.isclose(a,b,rtol=2e-4,atol=2e-4,equal_nan=False))
    return a == b


def buffers(root):
    paths = list(root.glob('*.rgba')) + list(root.glob('*.rgba.gz'))
    result = {p.name.removesuffix('.gz').removesuffix('.rgba'):p for p in paths}
    assert len(result) == len(paths), 'duplicate image keys'
    return result


def pixels(path):
    data = gzip.decompress(path.read_bytes()) if path.suffix == '.gz' else path.read_bytes()
    result = np.frombuffer(data,dtype='<f4')
    assert result.size == 128*128*4, f'{path}: wrong pixel count'
    return result


if __name__ == '__main__':
    ref, actual = map(Path, sys.argv[1:3])
    a,b = values(ref), values(actual)
    failures = [k for k in a if k not in b or not equal(a[k],b[k])]
    extra = b.keys()-a.keys()
    for k in failures[:40]: print(k, a[k], b.get(k,'MISSING'))
    print(f'records: {len(a)-len(failures)}/{len(a)}, {len(extra)} unexpected')
    ai,bi = buffers(ref),buffers(actual)
    expected = {k.removesuffix('.pixels') for k in a if k.endswith('.pixels')}
    assert set(ai)==expected, 'reference image manifest differs from records'
    image_failures = 0
    channels = matched = 0
    for k,p in sorted(ai.items()):
        x = pixels(p);channels+=x.size
        if k not in bi:
            image_failures+=1;print(k,'MISSING');continue
        y=pixels(bi[k]);ok=np.isclose(x,y,rtol=2e-4,atol=2e-4,equal_nan=False)
        matched+=int(ok.sum())
        if not ok.all():
            image_failures+=1
            print(k,f'{ok.sum()}/{ok.size} channels, max delta {np.max(np.abs(x-y))}')
    unexpected=set(bi)-set(ai)
    print(f'images: {len(ai)-image_failures}/{len(ai)}, {len(unexpected)} unexpected; channels: {matched}/{channels}')
    sys.exit(int(bool(failures or extra or image_failures or unexpected)))
