"""Reject incomplete or nonfinite Pitch Shifter captures."""
import math
from common import blocks, SUFFIXES, cases, devices, header, samples
MODES = ('Mono', 'Stereo', 'Quad', 'Surround', 'Mode5point1', 'Mode7point1', 'Prologic')


def verify(root, release, rate, contexts=False, selected=None):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    for case in selected if selected is not None else cases(contexts):
        if case.startswith('device_'):
            # Metadata only: every requested layout's getter, caps and active channels.
            for mode in MODES:
                for field in ('getter', 'caps', 'channels'):
                    assert isinstance(meta[f'{case}.{mode}.{field}'], int), (case, mode, field)
                assert meta[f'{case}.{mode}.channels'] > 0, (case, mode)
            continue
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            size = meta.get(case+'.buffer', 1024)
            mode = meta.get(case+'.mode', 2)
            channels = meta.get(case+'.render_channels') or {1: 1, 2: 2, 3: 4, 4: 5, 5: 6, 6: 8}[mode]
            if case.startswith('context_fx_'):
                assert channels == {1: 1, 2: 2, 3: 4, 4: 5, 5: 6, 6: 8}[mode], (case, 'inactive requested layout', channels)
            assert record == dict(file=case+suffix+'.f32', frames=blocks(case)*size, channels=channels), case+suffix
            values = samples(root, record)
            assert len(values) == blocks(case)*size*channels and all(map(math.isfinite, values)), case+suffix
    if not contexts and 'plain.master' in meta: assert any(abs(v)>0.01 for v in samples(root, meta['plain.master']))
    print('Complete finite capture:', root)
