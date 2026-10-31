#!/usr/bin/env python3
"""Compare every union key and value at the particle-stream 2e-4 tolerance."""
import argparse
import json
import math
from pathlib import Path

def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    result = {row['k']: row['v'] for row in rows}
    if len(rows) != len(result): raise ValueError('Duplicate keys: ' + str(path))
    return result

def differences(a, b, path=''):
    if isinstance(a, dict) and isinstance(b, dict):
        for key in sorted(a.keys() | b.keys()):
            if key not in a or key not in b:
                yield dict(path=path+'/'+key, expected=a.get(key, 'MISSING'), actual=b.get(key, 'MISSING'))
            else:
                yield from differences(a[key], b[key], path+'/'+key)
    elif isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        for i, (x, y) in enumerate(zip(a, b)):
            yield from differences(x, y, path+'/'+str(i))
    elif isinstance(a, (int,float)) and isinstance(b, (int,float)) and not isinstance(a,bool) and not isinstance(b,bool):
        if not math.isfinite(a) or not math.isfinite(b) or not math.isclose(a,b,abs_tol=2e-4,rel_tol=2e-4):
            yield dict(path=path, expected=a, actual=b)
    elif a != b:
        yield dict(path=path, expected=a, actual=b)

def report(reference, actual):
    failures = []
    for key in sorted(reference.keys() | actual.keys()):
        if key not in reference or key not in actual:
            failures.append(dict(key=key, path='', expected=reference.get(key,'MISSING'), actual=actual.get(key,'MISSING')))
        else:
            failures.extend(dict(key=key, **d) for d in differences(reference[key],actual[key]))
    bad = {f['key'] for f in failures}
    return dict(reference_records=len(reference), actual_records=len(actual),
                union_records=len(reference.keys()|actual.keys()), failing_records=len(bad),
                failing_values=len(failures), failures=failures)

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--unity-version',required=True)
    p.add_argument('--actual',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--reference',type=Path,help='Independent Native agreement or candidate control input')
    a = p.parse_args()
    reference = a.reference or Path(__file__).parent/'Reference'/('unity-'+a.unity_version+'.jsonl')
    result = report(load(reference),load(a.actual))
    a.output.parent.mkdir(parents=True,exist_ok=True)
    a.output.write_text(json.dumps(result,indent=2)+'\n')
    print({k:v for k,v in result.items() if k!='failures'})
    raise SystemExit(int(bool(result['failures'])))
