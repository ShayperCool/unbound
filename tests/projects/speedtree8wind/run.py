#!/usr/bin/env python3
"""The SpeedTree 8 wind constants trees draw with, frame by frame; see README.md.

The sample trees are not part of the repository: every run stages them into its private project
copy from $UNBOUND_SPEEDTREE_SAMPLES, else $XDG_CACHE_HOME/unbound/speedtree-samples (~/.cache by
default), and stops when they are not there.

    python3 tests/projects/speedtree8wind/run.py record --label A   # Editor capture (Linux, GPU)
    python3 tests/projects/speedtree8wind/run.py publish A B        # two identical captures
    python3 tests/projects/speedtree8wind/run.py gate               # the engine against Reference/
    python3 tests/projects/speedtree8wind/run.py gate --if-staged   # the same, a SKIPPED line without the samples
    (--profile first: the scene whose wind blows from the first frame; record and publish it on its own)
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

VARIABLE = 'SPEEDTREE_PROBE_OUT'
SAMPLE_SET = 'v8'


def samples():
    root = os.environ.get('UNBOUND_SPEEDTREE_SAMPLES')
    if not root:
        cache = os.environ.get('XDG_CACHE_HOME') or str(Path.home()/'.cache')
        root = str(Path(cache)/'unbound'/'speedtree-samples')
    source = Path(root)/SAMPLE_SET
    if not source.is_dir():
        raise SystemExit(f'speedtree8wind: the SpeedTree sample trees are not staged: no {source} '
                         '(set UNBOUND_SPEEDTREE_SAMPLES to a directory with v8/; they are never part of the repository)')
    return source


# The wind's option bytes in a SpeedTree 8 file: after its leading word and 11 floats, the ten
# 10-sample oscillation curves, the global height and exponent, two global curves, two branch
# levels (three curves and three floats each), the anchor, the first level's length, two leaf
# groups (five curves and two floats each), the fronds' curve and two floats (tests/projects/
# speedtree8's reader, SpeedTree8File.ReadWind).
WIND_OPTIONS = 4 + 11*4 + 10*40 + 2*4 + 2*40 + 2*(3*40 + 3*4) + 3*4 + 4 + 2*(5*40 + 2*4) + 40 + 2*4
BRANCH_DIRECTIONAL = (3, 9)
DIRECTIONAL = ('Broadleaf_Desktop_Field.st', 'Field_Directional.st', 'd1f2c3b4a5968778695a4b3c2d1e0f12')
# The SpeedTree 9 tree and its dump, from tests/projects/speedtree9wind (committed there).
SPEEDTREE9 = PROBE.parent/'speedtree9wind'/'Assets'
PROFILE_FILE = 'SpeedTree8WindProfile.txt'


def with_directional_branches(data):
    """The tree's bytes with both branch levels' adherence made directional (wind options 3 and 9),
    which no sample sets."""
    data = bytearray(data)
    root = len(b'SpeedTree___')
    wind = root + struct.unpack_from('<I', data, root + 4 + 4*7)[0]
    for option in BRANCH_DIRECTIONAL:
        data[wind + WIND_OPTIONS + option] = 1
    return bytes(data)


def stage(project, profile=None):
    source = samples()
    target = project/'Assets'/'Trees'/SAMPLE_SET
    shutil.copytree(source, target)
    tree, copy, guid = DIRECTIONAL
    (target/copy).write_bytes(with_directional_branches((source/tree).read_bytes()))
    meta, n = re.subn(r'(?m)^guid: \w+$', 'guid: '+guid, (source/(tree+'.meta')).read_text())
    if n != 1:
        raise SystemExit('speedtree8wind: the sample .meta has no single guid line')
    (target/(copy+'.meta')).write_text(meta)
    trees9 = project/'Assets'/'Trees9'
    trees9.mkdir()
    for f in sorted((SPEEDTREE9/'Trees').iterdir()):
        if f.name.startswith('Calm.st9') or f.suffix in ('.png',) or f.name.endswith('.png.meta'):
            shutil.copyfile(f, trees9/f.name)
    for name in ('WindDump.shader', 'WindDump.shader.meta'):
        shutil.copyfile(SPEEDTREE9/'Shaders'/name, project/'Assets'/'Shaders'/name)
    if profile:
        (project/PROFILE_FILE).write_text(profile+'\n')
    print(f'staged {sum(1 for p in target.rglob("*") if p.is_file())} sample files from {source}, '
          f'{copy} and the SpeedTree 9 tree Calm.st9' + (f' (profile {profile})' if profile else ''))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    parser.add_argument('--if-staged', action='store_true',
                        help='gate only where the samples are staged; elsewhere print SKIPPED and succeed')
    parser.add_argument('--profile', choices=('first',), default=None,
                        help='first: the scene whose wind blows from the first frame (gate runs both without it)')
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        samples()
        return value_probe.record(PROBE, 'ProbeRunner.Record', VARIABLE, label, True,
                                  configure=lambda project: stage(project, args.profile))
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels, profile=args.profile)
    try:
        samples()
    except SystemExit as absent:
        if not args.if_staged:
            raise
        print(f'speedtree8wind: SKIPPED, nothing compared: {absent}')
        return 0
    profiles = [args.profile] if args.profile else [None, 'first']
    code = 0
    for profile in profiles:
        engine = ['--frames', '1700', '--size', '64x64'] if profile is None else \
            ['--frames', '500', '--size', '64x64', '--scene', 'Assets/Scenes/FirstWind.unity']
        code |= value_probe.gate(PROBE, VARIABLE, label + ('-'+profile if profile else ''), 0, engine,
                                 configure=lambda project, p=profile: stage(project, p), profile=profile)
    return code


if __name__ == '__main__':
    sys.exit(main())
