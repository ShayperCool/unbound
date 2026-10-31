#!/usr/bin/env python3
"""Compare current complete-sequence motion with canonical Unity, retaining every key."""
import argparse
import collections
import importlib.util
import json
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, reference_root, run_root
spec = importlib.util.spec_from_file_location('comparison', ROOT.parent/'trigger2dtiming/report.py')
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('label', nargs='?', default='final')
    parser.add_argument('--work', type=Path, default=Path(os.environ.get('MOTION_WORK', run_root(ROOT))))
    parser.add_argument('--baseline', default='baseline')
    parser.add_argument('--baseline-work', type=Path)
    parser.add_argument('--suite', choices=['motion', 'rotation', 'skin'], default='motion')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    suffix = '.json' if args.suite == 'skin' else '.json.gz'
    native = reference_file(ROOT.parent/'colliderskin2d') if args.suite == 'skin' else reference_root(ROOT)/(args.suite+suffix)
    baseline = (args.baseline_work or args.work)/(args.baseline+'.json')
    candidate = args.work/(args.label+'.json')
    ref = comparison.load(native)
    old = comparison.load(baseline)
    new = comparison.load(candidate)
    result = {}
    for group, scenarios in [('motion', set(ref)|set(old)|set(new)),
                             ('broad', {k for d in [ref, old, new] for k in d if k.startswith(('2d.', '3d.'))})]:
        r, b, c = [{k:d[k] for k in scenarios if k in d} for d in [ref, old, new]]
        before = comparison.compare(r, b, 1e-3)
        after = comparison.changes(before, comparison.compare(r, c, 1e-3))
        result[group] = dict(before=before, after=after)
        comparison.describe(group, after)
        print('failure scenarios', dict(collections.Counter(x['scenario'] for x in after['failures'])), flush=True)
    result['inputs_sha256'] = {str(p.resolve()):comparison.sha(p) for p in [native, baseline, candidate]}
    output = args.output or run_root(ROOT, 'reports')/(args.label+'.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2)+'\n')
    print(output)
    if any(result[group]['after']['introduced'] for group in ['motion', 'broad']):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
