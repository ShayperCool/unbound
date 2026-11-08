"""Live effect profile: completeness and the unchanged audio comparator bounds."""
import math
import re
from common import FIXTURE, SUFFIXES, header, samples

BLOCKS = 24


def cases():
    return re.findall(r'new LiveCase\("([a-z0-9_]+)"', (FIXTURE/'Assets/Scripts/LiveEffectProbe.cs').read_text())


def verify(root, release, rate):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    assert set(meta) == {'release', 'rate', *(k+s for k in cases() for s in SUFFIXES)}
    for case in cases():
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=BLOCKS*1024, channels=2)
            values = samples(root, record)
            assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case+suffix
            assert any(abs(v) > 1e-6 for v in values), case+suffix
    print('Complete finite live capture:', root)


if __name__ == '__main__':
    import argparse
    from pathlib import Path
    from compare import compare
    p = argparse.ArgumentParser()
    p.add_argument('--release', required=True)
    p.add_argument('--rate', required=True, type=int, choices=(48000, 44100))
    p.add_argument('--reference', type=Path)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--exact', action='store_true')
    p.add_argument('--pcm-steps', type=int, choices=(0, 1), default=0)
    a = p.parse_args()
    reference = a.reference or FIXTURE/'Reference'/('unity-'+a.release)/('live-rate-'+str(a.rate))
    verify(reference, a.release, a.rate)
    keys = set(cases())
    for root in (reference, a.candidate):
        keys.update(field.split('.')[0] for field in header(root) if field not in ('release', 'rate'))
    failed = compare(reference, a.candidate, a.report, dict.fromkeys(sorted(keys)), exact=a.exact, pcm_steps=a.pcm_steps)
    verify(a.candidate, a.release, a.rate)
    raise SystemExit(bool(failed))
