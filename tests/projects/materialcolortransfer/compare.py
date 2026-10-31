#!/usr/bin/env python3
"""Strict finite CPU/GPU comparison, complete manifests, no expected failures.

Frozen before Native captures or runtime changes: abs/relative tolerance 2e-5.
Nonfinite API observations use explicit JSON strings; GPU inputs must be finite.
"""
import gzip
import json
import math
from pathlib import Path
import sys
import numpy as np

def values(root):
    rows=[json.loads(s) for s in (root/'values.jsonl').read_text().splitlines()]
    result={r['k']:r['v'] for r in rows}
    assert len(result)==len(rows) and rows, 'empty or duplicate records'
    return result

def equal(a,b):
    if isinstance(a,list):
        return isinstance(b,list) and len(a)==len(b) and all(equal(x,y) for x,y in zip(a,b))
    if isinstance(a,(int,float)) and not isinstance(a,bool):
        return isinstance(b,(int,float)) and math.isfinite(a) and math.isfinite(b) and math.isclose(a,b,rel_tol=2e-5,abs_tol=2e-5)
    return a==b

def buffers(root):
    paths=list(root.glob('*.rgba'))+list(root.glob('*.rgba.gz'))
    result={p.name.removesuffix('.gz').removesuffix('.rgba'):p for p in paths}
    assert len(result)==len(paths),'duplicate buffers'
    return result

def pixels(p):
    return np.frombuffer(gzip.decompress(p.read_bytes()) if p.suffix=='.gz' else p.read_bytes(),dtype='<f4')

def compare(ref,actual):
    a,b=values(ref),values(actual)
    failures=[k for k in a if k not in b or not equal(a[k],b[k])]
    for k in failures: print(k,a[k],b.get(k,'MISSING'))
    print(f'records: {len(a)-len(failures)}/{len(a)}, {len(b.keys()-a.keys())} unexpected')
    ai,bi=buffers(ref),buffers(actual)
    expected={k.removesuffix('.pixels') for k in a if k.endswith('.pixels')}
    assert ai.keys()==expected and expected,'reference manifest differs from records'
    failed=matched=channels=0
    for k,p in sorted(ai.items()):
        x=pixels(p);assert x.size==a[k+'.pixels']*4 and np.isfinite(x).all()
        channels+=x.size
        if k not in bi:failed+=1;print(k,'MISSING');continue
        y=pixels(bi[k])
        if x.size!=y.size:failed+=1;print(k,'WRONG SIZE');continue
        ok=np.isfinite(y)&(np.abs(x-y)<=np.maximum(2e-5,2e-5*np.maximum(np.abs(x),np.abs(y))))
        matched+=int(ok.sum())
        if not ok.all():
            failed+=1;print(k,f'{ok.sum()}/{ok.size} channels; native={x[:4].tolist()} actual={y[:4].tolist()} max delta={np.max(np.abs(x-y))}')
    print(f'images: {len(ai)-failed}/{len(ai)}, {len(bi.keys()-ai.keys())} unexpected; channels: {matched}/{channels}')
    return int(bool(failures or b.keys()!=a.keys() or failed or bi.keys()!=ai.keys()))
if __name__=='__main__':sys.exit(compare(*map(Path,sys.argv[1:3])))
