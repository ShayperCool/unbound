"""Reject incomplete, nonfinite or silent Distortion captures."""
import math
from common import BLOCKS, SUFFIXES, cases, header, samples

def verify(root, release, rate):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    for case in cases():
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=BLOCKS*1024, channels=2), case+suffix
            values = samples(root, record)
            assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case+suffix
            assert any(abs(v) > 1e-6 for v in values) or (suffix == "" and case in {"mix_dc_000001", "source_small"}), case+suffix
    print('Complete finite capture:', root)
