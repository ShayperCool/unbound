#!/usr/bin/env python3
"""What importing SpeedTree 8 trees gives; see README.md.

The trees are not part of the repository: every run stages them into its private project copy
from $UNBOUND_SPEEDTREE_SAMPLES, else $XDG_CACHE_HOME/unbound/speedtree-samples (~/.cache by
default), and stops when they are not there.

    python3 tests/projects/speedtree8/run.py record --label A   # Editor capture (macOS batch is fine)
    python3 tests/projects/speedtree8/run.py publish A B        # two identical captures
    python3 tests/projects/speedtree8/run.py gate               # the engine against Reference/
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
        text, n = re.subn(rf'(?m)^  {key}: .*$', f'  {key}: {value}', text)
        if n != 1:
            raise SystemExit(f'speedtree8: the sample .meta has no single {key}')
    if remaps is True:
        text = re.sub(r'(?ms)^  externalObjects:\n(  - .*?)(?=^  serializedVersion:)', '  externalObjects: {}\n', text)
    elif remaps == 'dangling':
        text, n = re.subn(r'guid: \w+, type: 2\}', 'guid: 0123456789abcdef0123456789abcdef, type: 2}', text)
        if n == 0:
            raise SystemExit('speedtree8: the sample .meta has no material remap')
    return text


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
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        samples()
        return value_probe.record(PROBE, 'ProbeRunner.Record', VARIABLE, label, True, macos_batch=True, configure=stage)
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    samples()
    return value_probe.gate(PROBE, VARIABLE, label, 0, ['--frames', '10', '--size', '64x64'], configure=stage)


if __name__ == '__main__':
    sys.exit(main())
