#!/usr/bin/env python3
"""Union-key comparisons preserve every failure and passing numeric difference."""
import argparse
import gzip
import hashlib
import json
import math
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, reference_root, run_root


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    path = Path(path)
    opener = gzip.open if path.suffix == '.gz' else open
    with opener(path, 'rt') as stream:
        return json.load(stream)


def compare(ref, candidate, tolerance):
    result = {'records': sum(len(ref.get(k, {}).keys() | candidate.get(k, {}).keys()) for k in ref.keys() | candidate.keys()),
              'reference_records': sum(map(len, ref.values())), 'candidate_records': sum(map(len, candidate.values())),
              'exact': 0, 'matched': 0,
              'rounding': [], 'failures': []}
    for scenario in sorted(ref.keys() | candidate.keys()):
        a, b = ref.get(scenario, {}), candidate.get(scenario, {})
        for key in sorted(a.keys() | b.keys()):
            x, y = a.get(key, '<missing>'), b.get(key, '<missing>')
            record = {'scenario': scenario, 'key': key, 'reference': x, 'candidate': y}
            if key in a and key in b and x == y:
                result['exact'] += 1
                result['matched'] += 1
            elif (isinstance(x, (int, float)) and not isinstance(x, bool)
                  and isinstance(y, (int, float)) and not isinstance(y, bool)
                  and math.isfinite(x) and math.isfinite(y)
                  and abs(x-y) <= tolerance*max(1, abs(x))):
                result['matched'] += 1
                result['rounding'].append(record)
            else:
                result['failures'].append(record)
    return result



def changes(before, after):
    bad = lambda result: {(r['scenario'], r['key']) for r in result['failures']}
    after.update(resolved=sorted(bad(before)-bad(after)),
                 introduced=sorted(bad(after)-bad(before)),
                 retained=sorted(bad(before)&bad(after)))
    return after


def describe(name, result):
    print(name, result['matched'], '/', result['records'], 'matched', result['exact'],
          'exact', len(result['rounding']), 'rounding', len(result['failures']), 'failures',
          *([str(len(result['introduced'])), 'introduced', str(len(result['retained'])), 'retained']
            if 'introduced' in result else []), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', type=Path, default=Path(os.environ.get('TRIGGER_WORK', run_root(ROOT))))
    parser.add_argument('--label', default='final')
    parser.add_argument('--baseline', default='baseline')
    parser.add_argument('--baseline-work', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    prior = args.baseline_work or args.work
    report = {}
    for name, native in [('timing', reference_root(ROOT)/'timing.json'),
                         ('order', reference_root(ROOT)/'order.json'),
                         ('physics', reference_file(ROOT.parent/'physics'))]:
        reference = load(native)
        before = compare(reference, load(prior/f'{name}-{args.baseline}.json'), 1e-3)
        after = changes(before, compare(reference, load(args.work/f'{name}-{args.label}.json'), 1e-3))
        report[name] = dict(before=before, after=after)
        describe(name, after)
    output = args.output or run_root(ROOT, 'reports')/(args.label+'.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2)+'\n')
    print(output)
    if any(r['after']['introduced'] for r in report.values()):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
