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
        if case.startswith('context_live_reset_'):
            after_width = {1:1,2:2,3:4,4:5,5:6,6:8}[meta[case+'.after_mode']]
            for segment, count, width in [('', 8, meta[case+'.render_channels']), ('.after', 24, after_width), ('.drain', 8, after_width)]:
                for suffix in SUFFIXES:
                    name = case+segment+suffix
                    record = meta[name]
                    assert record == dict(file=name+'.f32', frames=count*meta[case+'.buffer'], channels=width), name
                    values = samples(root, record)
                    assert len(values) == count*meta[case+'.buffer']*width and all(map(math.isfinite, values)), name
            assert meta[case+'.before_playing'] and meta[case+'.callbacks'] == 1
            assert not meta[case+'.callback_device'] and not meta[case+'.callback_playing']
            assert meta[case+'.before_clip_bits'] == 1048576000 and meta[case+'.callback_clip_bits'] == 0
            continue
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            size = meta.get(case+'.buffer', 1024)
            mode = meta.get(case+'.mode', 2)
            channels = meta.get(case+'.render_channels') or {1: 1, 2: 2, 3: 4, 4: 5, 5: 6, 6: 8}[mode]
            if case.startswith(('context_fx_', 'context_live_', 'context_wet_', 'context_wetedge_')):
                assert channels == {1: 1, 2: 2, 3: 4, 4: 5, 5: 6, 6: 8}[mode], (case, 'inactive requested layout', channels)
            assert record == dict(file=case+suffix+'.f32', frames=blocks(case)*size, channels=channels), case+suffix
            values = samples(root, record)
            assert len(values) == blocks(case)*size*channels and all(map(math.isfinite, values)), case+suffix
    if not contexts and 'plain.master' in meta: assert any(abs(v)>0.01 for v in samples(root, meta['plain.master']))
    print('Complete finite capture:', root)
