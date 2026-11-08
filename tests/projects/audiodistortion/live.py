"""Live effect profile: completeness and the unchanged audio comparator bounds."""
import math
import re
from common import FIXTURE, SUFFIXES, header, samples

BLOCKS = 24


def cases():
    return re.findall(r'new LiveCase\("([a-z0-9_]+)"', (FIXTURE/'Assets/Scripts/LiveEffectProbe.cs').read_text())


def verify(root, release, rate, selected=None):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    keys = cases() if selected is None else selected
    expected = {'release', 'rate', *(k+s for k in keys for s in SUFFIXES)}
    if selected is None:
        expected.add('sessions')
        assert meta['sessions'] == dict(sections())
    assert set(meta) == expected
    for case in keys:
        for suffix in SUFFIXES:
            record = meta[case+suffix]
            assert record == dict(file=case+suffix+'.f32', frames=BLOCKS*1024, channels=2)
            values = samples(root, record)
            assert len(values) == BLOCKS*1024*2 and all(map(math.isfinite, values)), case+suffix
            assert any(abs(v) > 1e-6 for v in values), case+suffix
    print('Complete finite live capture:', root)


# Each session begins with fresh device/mixer state. These boundaries are part of the
# capture context: the device sink also hears preceding mixers' tails within its session.
SECTION_ENDS = (
    ('kernel', 'chorus_listener'),
    ('wet_limits', 'pitch_wet_boundary'),
    ('gain_controls', 'pitch_parameter_wet'),
    ('gain_boundary', 'dist_wet_delta_bound_high'),
    ('follower', 'chorus_depth_down_source'),
    ('delay_lifecycle', 'chorus_zero_edit'),
)


def sections():
    keys = cases()
    start = 0
    result = []
    for label, last in SECTION_ENDS:
        end = keys.index(last) + 1
        assert end > start, label
        result.append((label, keys[start:end]))
        start = end
    assert start == len(keys) and len(keys) == len(set(keys))
    return result


def capture_sections(work, base_command, release, rate, author_project=False):
    """Replay the same independent sessions for Editor recording and engine validation."""
    import json
    import shutil
    import subprocess
    work.mkdir(parents=True, exist_ok=False)
    selected = sections()
    (work/'sections.json').write_text(json.dumps(dict(selected), indent=2)+'\n')
    failed = False
    for label, keys in selected:
        command = [*base_command, '--work', str(work/label), '--cases', ','.join(keys)]
        with (work/(label+'.log')).open('w') as log:
            failed |= subprocess.run(command, stdout=log, stderr=subprocess.STDOUT).returncode != 0
    target = work/'capture'
    target.mkdir()
    manifest = {}
    for label, _ in selected:
        source = work/label/'capture'
        for key, value in header(source).items():
            if key in manifest:
                assert key in ('release', 'rate') and manifest[key] == value
            manifest[key] = value
            if isinstance(value, dict) and 'file' in value:
                shutil.copyfile(source/value['file'], target/value['file'])
    manifest['sessions'] = dict(selected)
    (target/'capture.json').write_text(json.dumps(manifest)+'\n')
    if not failed:
        verify(target, release, rate)
    if author_project and not failed:
        for part in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(work/selected[0][0]/'project'/part, work/'project'/part)
    return int(failed)


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
        keys.update(field.split('.')[0] for field in header(root) if field not in ('release', 'rate', 'sessions'))
    failed = compare(reference, a.candidate, a.report, dict.fromkeys(sorted(keys)), exact=a.exact, pcm_steps=a.pcm_steps)
    verify(a.candidate, a.release, a.rate)
    raise SystemExit(bool(failed))
