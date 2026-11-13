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
        if case.startswith('context_schedule_'):
            assert meta[case+'.schedule_lead_frames'] == 8 * meta[case+'.buffer'], case
            expected_fraction = 1061158912 if case.endswith('threequarters') else 1048576000
            assert meta[case+'.schedule_fraction_bits'] == expected_fraction, case
            if '_late_' in case: assert meta[case+'.schedule_edit_block'] == 2, case
        if case.startswith(('context_live_reset_', 'context_recovery_', 'context_schedule_reset_')):
            after_width = {1:1,2:2,3:4,4:5,5:6,6:8}[meta[case+'.after_mode']]
            for segment, count, width in [('', 8, meta[case+'.render_channels']), ('.after', 24, after_width), ('.drain', 8, after_width)]:
                for suffix in SUFFIXES:
                    name = case+segment+suffix
                    record = meta[name]
                    assert record == dict(file=name+'.f32', frames=count*meta[case+'.buffer'], channels=width), name
                    values = samples(root, record)
                    assert len(values) == count*meta[case+'.buffer']*width and all(map(math.isfinite, values)), name
            if case.startswith('context_recovery_'):
                recover = not case.endswith('_idle')
                if recover:
                    assert meta[case+'.callback_elapsed_frames'] == 0, case
                    assert meta[case+'.callback_position'] == 0, case
                for block in range(24):
                    assert meta[f'{case}.render_frames_{block}'] == meta[case+'.buffer'], (case, block)
                    assert isinstance(meta[f'{case}.source_frames_{block}'], int) and meta[f'{case}.source_frames_{block}'] >= 0, (case, block)
            assert meta[case+'.before_playing'] and meta[case+'.callbacks'] == 1
            assert not meta[case+'.callback_device'] and not meta[case+'.callback_playing']
            assert meta[case+'.before_clip_bits'] == (1056964608 if case.startswith('context_schedule_') else 1048576000) and meta[case+'.callback_clip_bits'] == 0
            continue
        if case.startswith('context_scriptzone_'):
            width = int(case.split('_')[2])
            assert meta[case+'.render_channels'] == width, case
            assert meta[case+'.buffer'] == 1024 and meta[case+'.rate'] == rate, case
            if not case.endswith('_none'):
                assert meta[case+'.listener_gain_channels'] == width, case
            if width != 1:
                for block in [4, 12, 20]:
                    assert isinstance(meta[f'{case}.zone_update_{block}'], dict), (case, block)
        if case.startswith('context_monofx_'):
            mode = int(case.split('_')[2])
            assert meta[case+'.mode'] == mode and meta[case+'.render_channels'] == mode, case
            assert meta[case+'.buffer'] == 1024 and meta[case+'.rate'] == rate, case
            if case.endswith('_generator'): assert meta[case+'.generator_channels'] > 0, case
        if case.startswith('context_zone_'):
            phases = () if case.endswith('_listener_chorus_dry') else (8, 16) if case.endswith('_preset_control') else (4, 8, 16, 20, 24) if case.endswith('_overlap_move') else (4, 8, 16)
            for block in phases: assert isinstance(meta[f'{case}.zone_update_{block}'], dict), (case, block)
            if case.endswith('_radius_reference'):
                for block in phases: assert isinstance(meta[f'{case}.zone_refs_{block}'], dict), (case, block)
        if case.startswith(('context_prologic_', 'context_encoding_')):
            requested_mode = int(case.split('_')[2])
            assert meta[case+'.mode'] == requested_mode, case
            assert meta[case+'.render_channels'] == 2, case
            assert meta[case+'.rate'] == rate and meta[case+'.buffer'] == 1024, case
        if case.startswith('context_encoding_'):
            assert meta[case+'.input_channels'] == 6, case
            assert meta[case+'.input_frames'] == 32768, case
            assert meta[case+'.input_roundtrip'] is True, case
            assert meta[case+'.driver_caps'] == 6, case
            record = meta[case+'.input']
            assert record == dict(file=case+'.input.f32', frames=32768, channels=6), case
            values = samples(root, record)
            assert len(values) == 32768*6 and all(map(math.isfinite, values)), case
            if case.endswith('_impulses'):
                assert [(i, value) for i, value in enumerate(values) if value] == [
                    ((4096+2048*channel)*6+channel, 0.25) for channel in range(6)], case
            else:
                import hashlib
                columns = [values[channel::6] for channel in range(6)]
                assert all(sum(value != 0 for value in column) > 32000 for column in columns), case
                assert len({hashlib.sha256(column.tobytes()).digest() for column in columns}) == 6, case
        if case.startswith(('context_settings_', 'context_wide_')):
            _, _, requested_rate, requested_buffer, _ = case.split('_', 4)
            assert meta[case+'.rate'] == int(requested_rate), (case, 'unsupported requested sample rate')
            assert meta[case+'.buffer'] == int(requested_buffer), (case, 'unsupported requested DSP buffer')
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            size = meta.get(case+'.buffer', 1024)
            mode = meta.get(case+'.mode', 2)
            channels = meta.get(case+'.render_channels') or {1: 1, 2: 2, 3: 4, 4: 5, 5: 6, 6: 8}[mode]
            if case.startswith(('context_fx_', 'context_live_', 'context_wet_', 'context_wetedge_', 'context_settings_', 'context_zone_', 'context_monofx_', 'context_wide_', 'context_scriptzone_')):
                assert channels == {1: 1, 2: 2, 3: 4, 4: 5, 5: 6, 6: 8}[mode], (case, 'inactive requested layout', channels)
            assert record == dict(file=case+suffix+'.f32', frames=blocks(case)*size, channels=channels), case+suffix
            values = samples(root, record)
            assert len(values) == blocks(case)*size*channels and all(map(math.isfinite, values)), case+suffix
    if not contexts and 'plain.master' in meta: assert any(abs(v)>0.01 for v in samples(root, meta['plain.master']))
    print('Complete finite capture:', root)
