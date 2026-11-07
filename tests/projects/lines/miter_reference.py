#!/usr/bin/env python3
"""Publish the two identical reversal captures as new keys beside the old reference.

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


def load(directory):
    """The complete original, cap-count and reversal union, without filtering captures."""
    target = Path(directory)
    rows = layout.load_reference(target, 'capcount')
    extra, order = layout.lines_by_key((target/FILE).read_bytes())
    if order != KEYS or rows.keys() & extra.keys():
        raise SystemExit('reversal reference keys differ from the declared disjoint controls')
    rows.update({k: json.loads(v)['v'] for k, v in extra.items()})
    if (target/CHAIN_FILE).exists():
        extra, order = layout.lines_by_key((target/CHAIN_FILE).read_bytes())
        if order != CHAIN_KEYS or rows.keys() & extra.keys():
            raise SystemExit('chain reference keys differ from the declared disjoint controls')
        rows.update({k: json.loads(v)['v'] for k, v in extra.items()})
    return rows


def publish(release, label, receipt, chains=False):
    run = layout.run
    target = run.reference_root(run.PROBE)
    file, keys = (CHAIN_FILE, CHAIN_KEYS) if chains else (FILE, KEYS)
    receipt = layout.cache_artifact_path(receipt.resolve())
    if (target/file).exists() or (not chains and (target/(IMAGE+'.gz')).exists()):
        raise SystemExit('reversal reference already exists')
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
    if chains:
        previous, previous_order = layout.lines_by_key((target/FILE).read_bytes())
        if previous_order != KEYS or old.keys() & previous.keys():
            raise SystemExit('invalid existing reversal reference')
        old.update(previous)
        order += previous_order
    captured, captured_order = layout.lines_by_key((repeats[0]/'values.jsonl').read_bytes())
    if any(captured.get(k) != v for k, v in old.items()):
        raise SystemExit('existing reference records changed')
    if [k for k in captured_order if k not in old] != keys:
        raise SystemExit('new records differ from declared reversal controls')
    if not layout.known_order_delta(order, captured_order):
        raise SystemExit('old records moved beyond the known completion move')
    old_images = sorted(p.name.removesuffix('.gz') for p in target.glob('*.rgba.gz'))
    if run.images(repeats[0]) != sorted(old_images + ([] if chains else [IMAGE])):
        raise SystemExit('unexpected capture images')
    for name in old_images:
        if gzip.decompress((target/(name+'.gz')).read_bytes()) != (repeats[0]/name).read_bytes():
            raise SystemExit(f'existing image changed: {name}')
    data = b''.join(captured[k] for k in keys)
    before = layout.tree(target)
    layout.create_exclusive(target/file, data)
    if not chains:
        image = gzip.compress((repeats[0]/IMAGE).read_bytes(), mtime=0)
        layout.create_exclusive(target/(IMAGE+'.gz'), image)
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
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    publish(args.release, args.label, args.receipt, args.chains)
