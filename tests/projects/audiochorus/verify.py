"""Reject incomplete, nonfinite or silent Chorus captures."""
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
            assert len(values) == blocks(case)*1024*2 and all(map(math.isfinite, values)), case+suffix
            assert any(abs(v) > 1e-6 for v in values) or (case.startswith("tap3_") or case in {"dry0", "wet3", "source_tap3", "source_dc_0p00001", "mix_dc_0p00001"}), case+suffix
    print('Complete finite capture:', root)
