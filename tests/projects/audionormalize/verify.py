"""Reject incomplete or nonfinite Normalize captures."""
import math
from common import blocks, SUFFIXES, cases, header, samples
def verify(root, release, rate):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    for case in cases():
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=blocks(case)*1024, channels=2), case+suffix
            values = samples(root, record)
            assert len(values) == blocks(case)*2048 and all(map(math.isfinite, values)), case+suffix
    assert any(abs(v)>0.01 for v in samples(root, meta['plain.master']))
    print('Complete finite capture:', root)
