#!/usr/bin/env python3
"""Retain raw 3D measurements and gate explicit rejection of the unimplemented placements.

The dry controls expose common spatial playback differences. This is not a parity acceptance
for 3D filters: every raw failure stays in the report, and active 3D Distortion/Chorus must
report the unsupported placement and leave the dry source path unchanged.
"""
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
    for case in cases(profile):
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=BLOCKS*1024, channels=2), case+suffix
            values = samples(root, record)
            assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case+suffix
            silent = case in {f'spatial_dry_gain_{mode}_{distance}'
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
    # These controls isolate the common source path. Zone startup/completion remains an
    # explicit residual in the complete raw report, and components keep rejecting 3D sources.
    dry = [key for key in cases() if key.startswith('spatial_dry_')
           and key not in ('spatial_dry_zone_mono0', 'spatial_dry_zone_mono1')]
    measured = compare(reference, candidate, report.with_name('dry-'+report.name),
                       dict.fromkeys(dry), pcm_steps=1)
    assert measured == 0, 'measured dry spatial controls differ; see dry-'+report.name
    print(f'Spatial dry controls: {len(dry)} accepted at {rate} Hz')
    diagnostics = log.read_text()
    meta = header(candidate)
    rejected = 0
    for case in cases():
        if case.startswith('spatial_dry_'):
            assert "AudioSource on '"+case+"':" not in diagnostics, case
            continue
        effect = 'dist' if case.startswith('spatial_dist_') else 'chorus'
        component = 'AudioDistortionFilter' if effect == 'dist' else 'AudioChorusFilter'
        expected = "Unbound: AudioSource on '"+case+"': "+component+" on a 3D AudioSource is not supported:"
        assert diagnostics.count(expected) == 1, case+': expected one explicit placement diagnostic'
        dry = case.replace('spatial_'+effect+'_', 'spatial_dry_', 1)
        # The refusal must preserve the ordinary dry source, including mono and zone sends.
        for suffix in SUFFIXES:
            assert samples(candidate, meta[case+suffix]) == samples(candidate, meta[dry+suffix]), case+suffix
        rejected += 1
    print(f'UNSUPPORTED 3D placements: {rejected} explicit rejections, dry controls retained; '
          f'{failures} raw Native differences remain in {report}')


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
