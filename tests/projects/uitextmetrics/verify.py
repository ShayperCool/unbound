#!/usr/bin/env python3
"""Complete recursive union comparison, retaining every failure and before/after change."""
import argparse, json, math
from pathlib import Path

def flatten(value, key=''):
    if isinstance(value, dict):
        for k, v in value.items(): yield from flatten(v, key+'/'+k)
    elif isinstance(value, list):
        yield key+'/@length', len(value)
        for i, v in enumerate(value): yield from flatten(v, key+'/'+str(i))
    else: yield key, value

def load(path):
    result = {}
    for line in Path(path).read_text().splitlines():
        row = json.loads(line); key = row['key']
        if key in result: raise ValueError('duplicate '+key)
        result[key] = row
    return dict(flatten(result))

def compare(reference, candidate, tolerance=0.0001):
    missing = object(); failures = {}; count = 0
    for key in sorted(reference.keys() | candidate.keys()):
        count += 1
        a = reference.get(key, missing); b = candidate.get(key, missing)
        if isinstance(a, (float, int)) and isinstance(b, (float, int)):
            equal = a == b or (math.isnan(a) and math.isnan(b)) or abs(a-b) <= tolerance
        else: equal = a == b
        if not equal: failures[key] = dict(reference='<missing>' if a is missing else a, candidate='<missing>' if b is missing else b)
    return dict(observations=count, matched=count-len(failures), failed=len(failures), failures=failures)

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True, choices=['6000.0.71f1'])
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--before', type=Path)
    p.add_argument('--reference', type=Path)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    reference = load(a.reference or Path(__file__).parent/'Reference'/('unity-'+a.release+'.jsonl'))
    report = compare(reference, load(a.candidate))
    if a.before:
        before = compare(reference, load(a.before)); old = before['failures']; new = report['failures']
        report['before'] = before
        report['resolved'] = {k:v for k,v in old.items() if k not in new}
        report['introduced'] = {k:v for k,v in new.items() if k not in old}
        report['retained'] = {k:dict(before=old[k], after=new[k]) for k in old.keys() & new.keys()}
        report['changes'] = {k:len(report[k]) for k in ['resolved','introduced','retained']}
    # The output must remain in cache, just like the captures.
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
    from probe_paths import cache_artifact_path
    cache_artifact_path(a.output)
    a.output.write_text(json.dumps(report, indent=2, allow_nan=True)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k in ['observations','matched','failed','changes']}))
    return int(report['failed'] != 0)
if __name__ == '__main__': raise SystemExit(main())
