#!/usr/bin/env python3
"""What importing SpeedTree 8 trees gives; see README.md.

The trees are not part of the repository: every run stages them into its private project copy
from $UNBOUND_SPEEDTREE_SAMPLES, else $XDG_CACHE_HOME/unbound/speedtree-samples (~/.cache by
default), and stops when they are not there.

    python3 tests/projects/speedtree8/run.py record --label A   # Editor capture (macOS batch is fine)
    python3 tests/projects/speedtree8/run.py publish A B        # two identical captures
    python3 tests/projects/speedtree8/run.py gate               # the engine against Reference/
    python3 tests/projects/speedtree8/run.py gate --if-staged   # the same, a SKIPPED line without the samples

The landing gate (gate.json) runs `gate --if-staged`: where the samples are not staged it says so
and stops with success instead of failing every landing; everywhere else `gate` stops with an error.
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

VARIABLE = 'SPEEDTREE8_PROBE_OUT'
# The SpeedTree 8 samples the probe imports (each with its .meta, maps and materials as shipped):
# the files of the samples' v8/ folder, staged under Assets/Trees/v8/.
SAMPLE_SET = 'v8'


def samples():
    root = os.environ.get('UNBOUND_SPEEDTREE_SAMPLES')
    if not root:
        cache = os.environ.get('XDG_CACHE_HOME') or str(Path.home()/'.cache')
        root = str(Path(cache)/'unbound'/'speedtree-samples')
    source = Path(root)/SAMPLE_SET
    if not source.is_dir():
        raise SystemExit(f'speedtree8: the SpeedTree sample trees are not staged: no {source} '
                         '(set UNBOUND_SPEEDTREE_SAMPLES to a directory with v8/; they are never part of the repository)')
    return source


# Copies of sample trees the run makes beside them, with their .meta edited (and, for one, its
# collision objects moved), for the settings and contents the samples do not cover:
# (sample, copy, the copy's guid, .meta settings set, whether its material remaps are dropped,
#  collision objects whose second end is set to the first plus (0, dy, 0) in the file's units, as (index, dy)).
VARIANTS = [
    # Units left as they are with the samples' scale factor; embedded materials; cross-fading not
    # animated, with a fade-out width of its own; a selected wind quality the levels do not have.
    ('Card_Tree_Sample/card-tree-sample.st', 'Card_Tree_Sample/Card_Units0.st', 'a1b2c3d4e5f60718293a4b5c6d7e8f90',
     {'unitConversionEnumValue': '0', 'animateCrossFading': '0', 'fadeOutWidth': '0.4', 'selectedWindQuality': '2'}, True, ()),
    # Inches to metres; no smooth level transitions.
    ('Card_Tree_Sample/card-tree-sample.st', 'Card_Tree_Sample/Card_Inches.st', 'b1b2c3d4e5f60718293a4b5c6d7e8f90',
     {'unitConversionEnumValue': '3', 'enableSmoothLODTransition': '0'}, True, ()),
    # A custom scale factor.
    ('Card_Tree_Sample/card-tree-sample.st', 'Card_Tree_Sample/Card_Custom.st', 'c1b2c3d4e5f60718293a4b5c6d7e8f90',
     {'unitConversionEnumValue': '4', 'scaleFactor': '2'}, True, ()),
    # A material remap whose target is no asset.
    ('Card_Tree_Sample/card-tree-sample.st', 'Card_Tree_Sample/Card_Dangling.st', 'd1b2c3d4e5f60718293a4b5c6d7e8f90',
     {}, 'dangling', ()),
    # A collision object whose ends meet and one whose ends are 0.001 apart.
    ('Card_Tree_Sample/card-tree-sample.st', 'Card_Tree_Sample/Card_Spheres.st', 'e1b2c3d4e5f60718293a4b5c6d7e8f90',
     {}, True, ((0, 0.0), (1, 0.001))),
    # The leaves, bark and billboard materials embedded; cross-fading not animated, both widths set.
    ('Broadleaf_Desktop_Field.st', 'Field_Embedded.st', 'f1b2c3d4e5f60718293a4b5c6d7e8f90',
     {'animateCrossFading': '0', 'fadeOutWidth': '0.4', 'billboardTransitionCrossFadeWidth': '0.3'}, True, ()),
]


def collision_ends(data, edits):
    """The tree's bytes with collision objects' second ends moved to their first plus (0, dy, 0)
    (the file's table layout: root entry 4 is the collision objects, each a table whose entries 0
    and 1 are its two ends)."""
    data = bytearray(data)
    u = lambda at: struct.unpack_from('<I', data, at)[0]
    entry = lambda table, i: table + u(table + 4 + 4*i)
    collisions = entry(len(b'SpeedTree___'), 4)
    for index, dy in edits:
        collision = entry(collisions, index)
        x, y, z = struct.unpack_from('<3f', data, entry(collision, 0))
        struct.pack_into('<3f', data, entry(collision, 1), x, y + dy if dy else y, z)
    return bytes(data)


def variant_meta(text, guid, settings, remaps):
    text = re.sub(r'(?m)^guid: \w+$', 'guid: '+guid, text)
    for key, value in settings.items():
        text, n = re.subn(rf'(?m)^( +){key}: .*$', rf'\g<1>{key}: {value}', text)
        if n != 1:
            raise SystemExit(f'speedtree8: the sample .meta has no single {key}')
    if remaps is True:
        text = re.sub(r'(?ms)^  externalObjects:\n(  - .*?)(?=^  serializedVersion:)', '  externalObjects: {}\n', text)
    elif remaps == 'dangling':
        text, n = re.subn(r'guid: \w+, type: 2\}', 'guid: 0123456789abcdef0123456789abcdef, type: 2}', text)
        if n == 0:
            raise SystemExit('speedtree8: the sample .meta has no material remap')
    return text


PROFILE_MARKER = 'SpeedTree8Profile.txt'
# The fresh profile's own copies (beside the samples, which it stages without any .meta or
# material file): (sample, copy, its .meta as (guid, settings set, remaps dropped) or None,
# wind's leading word to set or None).
FRESH_VARIANTS = [
    # Without .meta: the wind's leading word 2 instead of the samples' 4.
    ('Card_Tree_Sample/card-tree-sample.st', 'Card_Tree_Sample/Card_Wind2.st', None, 2),
    # A tree with its .meta whose maps' .meta files lack what the import sets (below).
    ('Card_Tree_Sample/card-tree-sample.st', 'AlphaCheck/Card_Alpha.st', ('a2b2c3d4e5f60718293a4b5c6d7e8f90', {}, True), None),
    # The same with a colour map that is transparent but does not preserve coverage.
    ('Card_Tree_Sample/card-tree-sample.st', 'AlphaCheck2/Card_Alpha2.st', ('a5b2c3d4e5f60718293a4b5c6d7e8f90', {}, True), None),
]
# The maps of AlphaCheck/Card_Alpha.st, with the samples' .meta files edited: (sample, copy, guid,
# settings set).
FRESH_MAPS = [
    ('Card_Tree_Sample/card-tree-sample_Color.png', 'AlphaCheck/card-tree-sample_Color.png', 'a3b2c3d4e5f60718293a4b5c6d7e8f90',
     {'alphaIsTransparency': '0'}),
    ('Card_Tree_Sample/card-tree-sample_Extra.png', 'AlphaCheck/card-tree-sample_Extra.png', 'a4b2c3d4e5f60718293a4b5c6d7e8f90',
     {'sRGBTexture': '1'}),
    ('Card_Tree_Sample/card-tree-sample_Color.png', 'AlphaCheck2/card-tree-sample_Color.png', 'a6b2c3d4e5f60718293a4b5c6d7e8f90',
     {'mipMapsPreserveCoverage': '0'}),
    ('Card_Tree_Sample/card-tree-sample_Extra.png', 'AlphaCheck2/card-tree-sample_Extra.png', 'a7b2c3d4e5f60718293a4b5c6d7e8f90', {}),
]


def wind_leading(data, value):
    """The tree's bytes with the first word of its wind (root entry 7) set."""
    data = bytearray(data)
    u = lambda at: struct.unpack_from('<I', data, at)[0]
    root = len(b'SpeedTree___')
    struct.pack_into('<I', data, root + u(root + 4 + 4*7), value)
    return bytes(data)


def stage_fresh(project):
    """The samples without any .meta or material file (as a project first sees them), and the
    fresh profile's copies."""
    source = samples()
    target = project/'Assets'/'Trees'/SAMPLE_SET
    for f in sorted(source.rglob('*')):
        rel = f.relative_to(source)
        if not f.is_file() or f.suffix in ('.meta', '.mat') or any(p == 'Materials' or p.endswith(' Materials') for p in rel.parts[:-1]):
            continue
        (target/rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(f, target/rel)
    for tree, copy, meta, leading in FRESH_VARIANTS:
        data = (source/tree).read_bytes()
        (target/copy).parent.mkdir(parents=True, exist_ok=True)
        (target/copy).write_bytes(wind_leading(data, leading) if leading is not None else data)
        if meta:
            guid, settings, remaps = meta
            (target/(copy+'.meta')).write_text(variant_meta((source/(tree+'.meta')).read_text(), guid, settings, remaps))
    for texture, copy, guid, settings in FRESH_MAPS:
        shutil.copyfile(source/texture, target/copy)
        (target/(copy+'.meta')).write_text(variant_meta((source/(texture+'.meta')).read_text(), guid, settings, False))
    # Tells the recorder to record the .meta files the import writes (outside the assets).
    (project/PROFILE_MARKER).write_text('fresh\n')
    print(f'staged {sum(1 for p in target.rglob("*") if p.is_file())} files of the fresh profile from {source}')


PROFILES = {None: None, 'fresh': stage_fresh}


def stage(project):
    source = samples()
    target = project/'Assets'/'Trees'/SAMPLE_SET
    shutil.copytree(source, target)
    for tree, copy, guid, settings, remaps, ends in VARIANTS:
        data = (target/tree).read_bytes()
        (target/copy).write_bytes(collision_ends(data, ends) if ends else data)
        meta = (target/(tree+'.meta')).read_text()
        (target/(copy+'.meta')).write_text(variant_meta(meta, guid, settings, remaps))
    # The probe compares nothing of the staged files but what importing them gives.
    print(f'staged {sum(1 for p in target.rglob("*") if p.is_file())} sample files and variants from {source}')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    parser.add_argument('--profile', choices=[p for p in PROFILES if p], default=None,
                        help='fresh: the samples without .meta files (what a first import writes)')
    parser.add_argument('--if-staged', action='store_true',
                        help='gate only where the samples are staged; elsewhere print SKIPPED and succeed')
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    configure = PROFILES[args.profile] or stage
    if args.mode == 'record':
        samples()
        return value_probe.record(PROBE, 'ProbeRunner.Record', VARIABLE, label, True, macos_batch=True, configure=configure)
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels, profile=args.profile)
    try:
        samples()
    except SystemExit as absent:
        if not args.if_staged:
            raise
        print(f'speedtree8: SKIPPED, nothing compared: {absent}')
        return 0
    return value_probe.gate(PROBE, VARIABLE, label, 0, ['--frames', '10', '--size', '64x64'], configure=configure,
                            profile=args.profile, residuals_file=f'residuals-{args.profile}.json' if args.profile else 'residuals.json')


if __name__ == '__main__':
    sys.exit(main())
