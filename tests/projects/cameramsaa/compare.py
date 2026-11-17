#!/usr/bin/env python3
"""Compare the complete oracle: keys, image manifest, and every float channel.

    compare.py REFERENCE ACTUAL [--residuals RESIDUALS.json]

With --residuals (tests/residuals.py) the exit status is nonzero only when a difference is not
listed there within its bounds: a record's "actual" value (or "missing": true when the capture
lacks it), an image's failing "channels" and largest difference "max_delta". The strict counts are
printed as before.
"""
import gzip
import json
import math
import sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import residuals as residual_lists  # noqa: E402


def values(root):
    rows = [json.loads(s) for s in (root/'values.jsonl').read_text().splitlines()]
    result = {r['k']: r['v'] for r in rows}
    assert len(rows) == len(result), 'duplicate observation keys'
    return result


def equal(a, b):
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(equal(x, y) for x, y in zip(a, b))
    if isinstance(a, (int,float)) and isinstance(b, (int,float)):
        return math.isfinite(a) and math.isfinite(b) and math.isclose(a,b,rel_tol=2e-4,abs_tol=2e-4)
    return a == b


def buffers(root):
    paths = list(root.glob('*.rgba')) + list(root.glob('*.rgba.gz'))
    result = {p.name.removesuffix('.gz').removesuffix('.rgba'):p for p in paths}
    assert len(result) == len(paths), 'duplicate image keys'
    return result


def pixels(path, count=128*128):
    data = gzip.decompress(path.read_bytes()) if path.suffix == '.gz' else path.read_bytes()
    result = np.frombuffer(data,dtype='<f4')
    assert result.size == count*4, f'{path}: wrong pixel count'
    return result


if __name__ == '__main__':
    args = sys.argv[1:]
    listed = None
    if '--residuals' in args:
        at = args.index('--residuals')
        listed = residual_lists.load(args[at+1])
        del args[at:at+2]
    ref, actual = map(Path, args[:2])
    verdict = residual_lists.Gate(listed or {})
    a,b = values(ref), values(actual)
    failures = [k for k in a if k not in b or not equal(a[k],b[k])]
    extra = b.keys()-a.keys()
    for k in a:
        if k not in b: verdict.differs(k, {'missing': True})
        elif k in failures: verdict.differs(k, {'actual': b[k]})
        else: verdict.exact_key(k)
    for k in sorted(extra): verdict.missing(k, 'unexpected')
    # With a residual list the gate judges absent records (the suite reads MISSING as a capture
    # that is not there).
    for k in failures[:40]: print(k, a[k], b.get(k, 'MISSING' if listed is None else 'absent'))
    print(f'records: {len(a)-len(failures)}/{len(a)}, {len(extra)} unexpected')
    ai,bi = buffers(ref),buffers(actual)
    expected = {k.removesuffix('.pixels') for k in a if k.endswith('.pixels')}
    assert set(ai)==expected, 'reference image manifest differs from records'
    image_failures = 0
    channels = matched = 0
    for k,p in sorted(ai.items()):
        x = pixels(p,a[k+'.pixels']);channels+=x.size
        if k not in bi:
            image_failures+=1;print(k,'MISSING');verdict.missing(k+'.image','missing');continue
        y=pixels(bi[k],a[k+'.pixels'])
        with np.errstate(invalid='ignore'):
            delta=np.abs(x-y)
            ok=(x==y) | (np.isfinite(x)&np.isfinite(y)&(delta<=np.maximum(2e-4,2e-4*np.maximum(np.abs(x),np.abs(y)))))
        matched+=int(ok.sum())
        if not ok.all():
            image_failures+=1
            print(k,f'{ok.sum()}/{ok.size} channels, max delta {np.max(delta[~ok])}')
            verdict.differs(k+'.image', {'channels': int(ok.size-ok.sum()), 'max_delta': float(np.nanmax(np.where(ok, 0, np.nan_to_num(delta, nan=np.inf))))})
        else:
            verdict.exact_key(k+'.image')
    pngs={p.stem for p in actual.glob('*.png')}
    assert pngs==expected, 'captured PNG manifest differs from records'
    unexpected=set(bi)-set(ai)
    for k in sorted(unexpected): verdict.missing(k+'.image', 'unexpected')
    if listed is not None:
        verdict.finish(set(a) | {k+'.image' for k in ai})
        print('\n'.join(verdict.lines()))
    print(f'images: {len(ai)-image_failures}/{len(ai)}, {len(unexpected)} unexpected; channels: {matched}/{channels}')
    if listed is not None:
        sys.exit(int(not verdict.ok))
    sys.exit(int(bool(failures or extra or image_failures or unexpected)))
