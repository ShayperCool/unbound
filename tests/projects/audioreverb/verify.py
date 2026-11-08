"""Reject incomplete, nonfinite or silent purported reverb filter/zone measurements."""
import math
from common import BLOCKS, GATE_ZONE, SUFFIXES, FIXTURE, cases, header, samples
import re


def verify(root, release, rate, zones=True):
    """zones=False (engine runs while no zone case is gated) skips the zone reverb tail check."""
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    for key in ('filter.default', 'zone.default', 'filter.preset.User', 'zone.preset.User'):
        assert isinstance(meta.get(key), str), key
    source = (FIXTURE/'Assets/Scripts/AudioReverbProbe.cs').read_text()
    routed_cases = {row[0] for line in source.splitlines() if 'new[] {' in line and '"routed"' in line
                    if (row := re.findall(r'"([^"]*)"', line))}
    for case in cases():
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=BLOCKS*1024, channels=2), case+suffix
            values = samples(root, record)
            assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case+suffix
            # Zone cases play to the default output: the mixer sink stays silent.
            routed = case in routed_cases
            silent = (case.startswith('zone') and suffix and not routed) or ('_s1_' in case and case.endswith(('_d20', '_d25')))
            if silent: assert all(v == 0 for v in values), case+suffix
            else: assert any(abs(v) > 1e-6 for v in values), case+suffix
    for key in ('zone_lifecycle_pause_mono1', 'zone_lifecycle_pauseactivate_mono1'):
        assert meta[key+'.pausedPlaying'] is False, key
        assert meta[key+'.resumedPlaying'] is True, key
        dry = samples(root, meta[key+'.master'])
        assert all(v == 0 for v in dry[9*1024*2:12*1024*2]), key
        assert any(abs(v) > 1e-5 for v in dry[13*1024*2:]), key
    # A reverb case must carry energy after the dry impulses (frames 2048, 3072 and 5120).
    for case, suffix in (('generic', '.master'), ('listener', ''), ('zone_in', ''))[:3 if zones else 2]:
        values = samples(root, meta[case+suffix])
        assert any(abs(values[i]) > 1e-4 for i in range(2*6000, 2*BLOCKS*1024)), 'No reverb tail: '+case
    print('Complete finite capture:', root)
