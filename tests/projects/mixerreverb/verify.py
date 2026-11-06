"""Reject incomplete, nonfinite or silent purported SFX Reverb measurements."""
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
            # Device PCM also carries the still-ringing mixers of earlier cases; the silent group's
            # own sink holds only values below 1e-12.
            if case == 'silence': assert suffix == '' or all(abs(v) < 1e-12 for v in values), case+suffix
            # Both paths of dry_m10000 are at -100 dB: only the float sink keeps them.
            elif case == 'dry_m10000': assert suffix == '' or any(v != 0 for v in values), case+suffix
            else: assert any(abs(v) > 1e-6 for v in values), case+suffix
    # A wet case must carry reverb energy after the dry impulses (frames 2048, 3072 and 5120).
    for case in ('room0', 'wet_only', 'preset'):
        values = samples(root, meta[case+'.master'])
        assert any(abs(values[i]) > 1e-4 for i in range(2*6000, 2*BLOCKS*1024)), 'No reverb tail: '+case
    print('Complete finite capture:', root)
