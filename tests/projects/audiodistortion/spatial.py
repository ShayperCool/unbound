#!/usr/bin/env python3
"""Compare every measured 3D source and component placement against the immutable reference."""
import argparse
import math
from pathlib import Path
import re
from common import BLOCKS, FIXTURE, SUFFIXES, header, samples


def cases(profile="spatial"):
    source = (FIXTURE/'Assets/Scripts/EffectProbe.cs').read_text()
    body = source.split('SpatialCases = {', 1)[1].split('};', 1)[0]
    names = re.findall(r'new\[\] \{ "([a-z0-9_]+)"', body)
    return [key for key in names if key.startswith('spatial_dry_velocity_')] if profile.startswith('spatial-factor') else names


def verify(root, release, rate, profile="spatial"):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    if profile == 'spatial':
        assert math.isfinite(meta['captureDeltaTime']) and 0 < meta['captureDeltaTime'] <= 1024/rate
    for case in cases(profile):
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=BLOCKS*1024, channels=2), case+suffix
            values = samples(root, record)
            assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case+suffix
            silent = (suffix == '.master' and '_unrouted_' in case) or case in {f'spatial_dry_gain_{mode}_{distance}'
                              for mode in ('linear', 'custom') for distance in (20, 30, 100)}
            if silent:
                assert all(v == 0 for v in values), case+suffix
            else:
                assert any(abs(v) > 1e-6 for v in values), case+suffix
    print('Complete finite spatial measurement:', root)


def gate(candidate, log, report, release, rate):
    from compare import compare
    verify(candidate, release, rate)
    reference = FIXTURE/'Reference'/('unity-'+release)/('spatial-rate-'+str(rate))
    # Deliberately report every mismatch, with the unchanged comparator tolerances.
    failures = compare(reference, candidate, report, dict.fromkeys(cases()))
    accepted = compare(reference, candidate, report.with_name('gate-'+report.name),
                       dict.fromkeys(cases()), pcm_steps=1)
    assert accepted == 0, 'spatial playback or component output differs; see gate-'+report.name
    diagnostics = log.read_text()
    for case in cases():
        assert "AudioSource on '"+case+"':" not in diagnostics, case
    print(f'Spatial playback and components: {len(cases())} cases accepted at {rate} Hz; '
          f'{failures} strict differences retained in {report}')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(48000, 44100), required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--log', type=Path, required=True)
    p.add_argument('--report', type=Path, required=True)
    a = p.parse_args()
    gate(a.candidate, a.log, a.report, a.release, a.rate)


def factor_gate(candidate, report, release, rate, profile):
    from compare import compare
    verify(candidate, release, rate, profile)
    reference = FIXTURE/'Reference'/('unity-'+release)/(profile+'-rate-'+str(rate))
    assert compare(reference, candidate, report, dict.fromkeys(cases(profile)), pcm_steps=1) == 0
