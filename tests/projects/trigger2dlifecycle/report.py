#!/usr/bin/env python3
"""Compare all lifecycle controls and preserve every retained or introduced failure."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, reference_root, run_root
spec = importlib.util.spec_from_file_location('timing_compare', ROOT.parent/'trigger2dtiming/report.py')
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('label', nargs='?', default='final')
    parser.add_argument('--work', type=Path, default=Path(os.environ.get('TRIGGER_WORK', run_root(ROOT))))
    parser.add_argument('--baseline-work', type=Path)
    parser.add_argument('--baseline', default='baseline')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--accept', action='store_true')
    args = parser.parse_args()
    prior = args.baseline_work or args.work
    result = {}
    for name, native in [
        ('lifecycle', reference_root(ROOT)/'lifecycle.json'),
        ('receivers', reference_root(ROOT)/'receivers.json'),
        ('timing', reference_root(ROOT.parent/'trigger2dtiming')/'timing.json'),
        ('order', reference_root(ROOT.parent/'trigger2dtiming')/'order.json'),
        ('physics', reference_file(ROOT.parent/'physics'))]:
        ref = comparison.load(native)
        before = comparison.compare(ref, comparison.load(prior/f'{name}-{args.baseline}.json'), 1e-3)
        after = comparison.changes(before, comparison.compare(ref, comparison.load(args.work/f'{name}-{args.label}.json'), 1e-3))
        after['baseline'] = before
        result[name] = after
        comparison.describe(name, after)
    output = args.output or run_root(ROOT, 'reports')/(args.label+'.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2)+'\n')
    print(output)
    assert all(not r['introduced'] for r in result.values()), 'new failing observations'
    if args.accept:
        assert all(not result[n]['failures'] for n in ['lifecycle', 'receivers', 'timing', 'order'])
        assert all(result[n]['exact'] == result[n]['reference_records'] for n in ['lifecycle', 'receivers'])
        assert not any(r['key'].endswith(('/step', '/kind', '/phase', '/self', '/other', '/sleep', '/touching'))
                       for n in ['lifecycle', 'receivers', 'timing', 'order'] for r in result[n]['rounding'])


if __name__ == '__main__':
    main()
