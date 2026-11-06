"""Reject incomplete, nonfinite or silent purported Compressor, ParamEQ and simple filter measurements."""
import math
from common import BLOCKS, SUFFIXES, cases, header, samples

# Highpass Simple at the top of its cutoff range is measured silent.
SILENT = {'hps_22000'}
# Highpass Simple at 21999 Hz leaves its master floats below the 16-bit device step.
QUIET = {'hps_21999'}


def verify(root, release, rate):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    for case in cases():
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=BLOCKS*1024, channels=2), case+suffix
            values = samples(root, record)
            assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case+suffix
            assert (case in SILENT) != any(abs(v) > 1e-6 for v in values) or case+suffix in QUIET, case+suffix
    # The -20 dB threshold must compress the 0.5 step (frames 4096..16384) on the left.
    values = samples(root, meta['comp_step_thr20.master'])
    assert abs(values[2*2048] - 0.05) < 0.01 and values[2*12000] < 0.4, 'No compression: comp_step_thr20'
    print('Complete finite capture:', root)
