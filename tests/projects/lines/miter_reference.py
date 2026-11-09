#!/usr/bin/env python3
"""Publish two identical captures' new control keys beside the old reference.

Existing reference records and images must reproduce byte for byte. Only the known
completion-marker move is accepted. No existing reference file is written.
"""
import argparse
import gzip
import json
import os
from pathlib import Path

import capcount_layout as layout

FILE = 'miterrev.values.jsonl'
IMAGE = 'miterrev.image.rgba'
NAMES = [f'angle{a}' for a in ('170', '172', '174', '176', '178', '179', '179.5', '179.9', '180', '181')]
NAMES += ['narrow', 'wide', 'short', 'long', 'asymmetric', 'neighbors', 'shortNeighbor', 'perspective', 'transformZ']
KEYS = []
for name in NAMES:
    for corners in (0, 2):
        for renderer in ('line', 'trail'):
            KEYS += [f'miterrev.{name}.c{corners}.{renderer}.{part}' for part in ('inputs', 'mesh')]
            if name == 'angle179' and corners == 0 and renderer == 'line':
                KEYS.append('miterrev.image.pixels')

CHAIN_FILE = 'miterchain.values.jsonl'
CHAIN_NAMES = ['asymForward', 'asymBackward', 'api2Constant', 'api2Taper',
               'api2EmittingConstant', 'api2EmittingTaper', 'prefixShort', 'prefixLong',
               'prefixLongReversed', 'twoReversals', 'lagVsMinimum', 'shortPrefix90',
               'shortPrefix140', 'shortPrefix10']
CHAIN_KEYS = [f'miterchain.{name}.c{corners}.{renderer}.{part}' for name in CHAIN_NAMES
              for corners in (0, 2) for renderer in ('line', 'trail') for part in ('inputs', 'mesh')]


SPACE_FILE = 'space.values.jsonl'
SPACE_KEYS = []
for name in ['trail.far', 'trail.farTurned.u0', 'trail.farTurned.u1',
             *[f'line.scaledZ.{space}.u{u}' for space in ('local', 'world') for u in (0, 1)],
             'line.viewTransform.u0', 'line.viewTransform.u1', 'line.orthoNear0.3', 'line.orthoNear5',
             'line.behind']:
    SPACE_KEYS += [f'space.{name}.inputs', f'space.{name}.mesh']
for name in ('beyond', 'zigzag', 'closed', 'tie', 'two', 'one'):
    SPACE_KEYS += [f'simplify.{name}.inputs', f'simplify.{name}.positions']
SPACE_KEYS += [f'simplify.utility.{name}' for name in ('indices', 'points', 'indices2', 'points2')]

# The control files beside the canonical reference, in publication order: each extends the
# capture after the previous ones and is published only once those exist.
SETS = {'reversal': (FILE, KEYS, IMAGE), 'chain': (CHAIN_FILE, CHAIN_KEYS, None),
        'space': (SPACE_FILE, SPACE_KEYS, None)}
ORDER = list(SETS)


def load(directory):
    """The complete original, cap-count and control union, without filtering captures."""
    target = Path(directory)
    rows = layout.load_reference(target, 'capcount')
    for name in ORDER:
        file, keys, _ = SETS[name]
        if name != 'reversal' and not (target/file).exists():
            break
        extra, order = layout.lines_by_key((target/file).read_bytes())
        if order != keys or rows.keys() & extra.keys():
            raise SystemExit(f'{name} reference keys differ from the declared disjoint controls')
        rows.update({k: json.loads(v)['v'] for k, v in extra.items()})
    return rows


def publish(release, label, receipt, chains=False, extension=None):
    run = layout.run
    target = run.reference_root(run.PROBE)
    name = extension or ('chain' if chains else 'reversal')
    file, keys, image = SETS[name]
    receipt = layout.cache_artifact_path(receipt.resolve())
    if (target/file).exists() or (image and (target/(image+'.gz')).exists()):
        raise SystemExit(f'{name} reference already exists')
    repeats = [run.run_root(run.PROBE, label)/r for r in run.REPEATS]
    for repeat in repeats:
        problem = run.editor_problem(repeat) or run.capture_problem(repeat)
        if problem:
            raise SystemExit(f'{repeat.name}: {problem}')
    problems = run.differences(repeats, lambda d: ['values.jsonl', *run.images(d)])
    if problems:
        raise SystemExit(f'independent repeats differ: {problems}')
    binding = layout.source_binding(repeats, run.PROBE, release)
    old, order = layout.lines_by_key((target/'values.jsonl').read_bytes())
    cap, cap_order = layout.control_rows(target, old)
    old.update(cap)
    order += cap_order
    for previous_name in ORDER[:ORDER.index(name)]:
        previous_file, previous_keys, _ = SETS[previous_name]
        if not (target/previous_file).exists():
            raise SystemExit(f'publish the {previous_name} reference first')
        previous, previous_order = layout.lines_by_key((target/previous_file).read_bytes())
        if previous_order != previous_keys or old.keys() & previous.keys():
            raise SystemExit(f'invalid existing {previous_name} reference')
        old.update(previous)
        order += previous_order
    captured, captured_order = layout.lines_by_key((repeats[0]/'values.jsonl').read_bytes())
    if any(captured.get(k) != v for k, v in old.items()):
        raise SystemExit('existing reference records changed')
    if [k for k in captured_order if k not in old] != keys:
        raise SystemExit(f'new records differ from declared {name} controls')
    if not layout.known_order_delta(order, captured_order):
        raise SystemExit('old records moved beyond the known completion move')
    old_images = sorted(p.name.removesuffix('.gz') for p in target.glob('*.rgba.gz'))
    if run.images(repeats[0]) != sorted(old_images + ([image] if image else [])):
        raise SystemExit('unexpected capture images')
    for name in old_images:
        if gzip.decompress((target/(name+'.gz')).read_bytes()) != (repeats[0]/name).read_bytes():
            raise SystemExit(f'existing image changed: {name}')
    data = b''.join(captured[k] for k in keys)
    before = layout.tree(target)
    layout.create_exclusive(target/file, data)
    if image:
        layout.create_exclusive(target/(image+'.gz'), gzip.compress((repeats[0]/image).read_bytes(), mtime=0))
    after = layout.tree(target)
    assert {k: after[k] for k in before} == before
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps(dict(release=release, label=label, binding=binding,
        repeats={r.name: layout.sha((r/'values.jsonl').read_bytes()) for r in repeats},
        before=before, after=after, records=len(keys)), indent=2)+'\n')
    print(f'published {len(keys)} {file} records; {len(old)} old records unchanged')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    parser.add_argument('--receipt', required=True, type=Path)
    parser.add_argument('--chains', action='store_true', help='publish the later reach-history controls')
    parser.add_argument('--space', action='store_true', help='publish the build-space and Simplify controls')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    publish(args.release, args.label, args.receipt, args.chains, 'space' if args.space else None)
