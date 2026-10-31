#!/usr/bin/env python3
"""Compare every old/new key with the unchanged timing comparator; preserve all failures."""
import argparse
import collections
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parent
WORK = Path(os.environ['TRIGGER_WORK'])
spec = importlib.util.spec_from_file_location('timing_compare', ROOT.parent/'trigger2dtiming/report.py')
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('label')
    parser.add_argument('--accept', action='store_true')
    args = parser.parse_args()
    result = {}
    for name, native in [
        ('lifecycle', WORK/'lifecycle-native1.json'),
        ('receivers', WORK/'receivers-native1.json'),
        ('timing', ROOT.parent/'trigger2dtiming/Evidence/accepted/timing-expanded-native1.json'),
        ('order', ROOT.parent/'trigger2dtiming/Evidence/order/order-native1.json'),
        ('physics', ROOT.parent/'physics/Reference/unity-6000.0.71f1.json')]:
        candidate = WORK/f'{name}-{args.label}.json'
        if not candidate.exists(): continue
        baseline = WORK/f'{name}-baseline.json'
        if not baseline.exists(): baseline = ROOT.parent/f'trigger2dtiming/Evidence/accepted/{name}-final.json'
        ref, old, new = [json.loads(p.read_text()) for p in [native, baseline, candidate]]
        before, after = [comparison.compare(ref, data, 1e-3) for data in [old, new]]
        bad = lambda report: {(r['scenario'], r['key']) for r in report['failures']}
        after.update(resolved=sorted(bad(before)-bad(after)), introduced=sorted(bad(after)-bad(before)),
                     retained=sorted(bad(after)&bad(before)), baseline=before)
        result[name] = after
        print(name, after['matched'], '/', after['records'], 'exact', after['exact'],
              'resolved', len(after['resolved']), 'introduced', len(after['introduced']),
              'retained', len(after['retained']), flush=True)
        if after['failures']:
            print('failures in', len(set(r['scenario'] for r in after['failures'])), 'scenarios', flush=True)
    dest = ROOT/'Evidence'/args.label
    dest.mkdir(exist_ok=True)
    for name in result:
        for label in ['baseline', args.label]:
            src = WORK/f'{name}-{label}.json'
            if src.exists(): shutil.copy2(src, dest/src.name)
    for src in WORK.glob(f'*{args.label}*.log'): shutil.copy2(src, dest/src.name)
    (dest/'summary.json').write_text(json.dumps(result, indent=2)+'\n')
    if args.accept:
        assert all(not r['introduced'] for r in result.values())
        assert all(not result[n]['failures'] for n in ['lifecycle', 'receivers', 'timing', 'order'])
        assert all(result[n]['exact'] == result[n]['reference_records'] for n in ['lifecycle', 'receivers'])
        assert not any(r['key'].endswith(('/step', '/kind', '/phase', '/self', '/other', '/sleep', '/touching'))
                       for n in ['lifecycle', 'receivers', 'timing', 'order'] for r in result[n]['rounding'])


if __name__ == '__main__': main()
