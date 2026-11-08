"""Reject incomplete, nonfinite or silent purported Duck Volume/Send/Receive measurements."""
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
            assert any(abs(v) > 1e-6 for v in values), case+suffix
    for case in cases():
        if case + '.listener' not in meta:
            continue
        record = meta[case + '.listener']
        assert record == dict(file=case + '.listener.f32', frames=BLOCKS*1024, channels=2), case
        values = samples(root, record)
        assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case
        assert any(abs(v) > 1e-6 for v in values), case
    # The default sidechain step (frames 4096..16384) must duck the 0.25 music DC on the left.
    values = samples(root, meta['default.master'])
    assert values[2*2048] > 0.24 and values[2*12000] < 0.2, 'No ducking: default'
    print('Complete finite capture:', root)
