"""Complete rendered velocity controls on the ordinary fixed and dynamic player loops."""
import math
import re
from pathlib import Path
from common import FIXTURE, BLOCKS, SUFFIXES, header, samples

def cases():
    source = (FIXTURE/'Assets/Scripts/AudioMotionDriver.cs').read_text()
    return re.findall(r'"((?:source|listener)_[a-z_]+)"', source.split('MotionCases = {', 1)[1].split('};', 1)[0])

def verify(root, release, rate):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    assert isinstance(meta.get('fixedDeltaTime'), int), 'missing fixed-step getter bits'
    for case in cases():
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=BLOCKS*1024, channels=2), case+suffix
            values = samples(root, record)
            assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case+suffix
            assert any(abs(v) > 1e-6 for v in values), case+suffix
    print('Complete finite motion capture:', root)


if __name__ == '__main__':
    import argparse
    from compare import compare
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--reference', type=Path)
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--exact', action='store_true')
    a = p.parse_args()
    verify(a.candidate, a.release, a.rate)
    reference = a.reference or FIXTURE/'Reference'/('unity-'+a.release)/('motion-rate-'+str(a.rate))
    verify(reference, a.release, a.rate)
    failed = compare(reference, a.candidate, a.report, dict.fromkeys(cases()), exact=a.exact,
                     pcm_steps=0 if a.exact else 1, prefixes=('fixedDeltaTime',))
    raise SystemExit(bool(failed))
