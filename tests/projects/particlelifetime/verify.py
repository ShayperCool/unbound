#!/usr/bin/env python3
"""Compare complete lifetime probe unions and require exact independent Native agreement."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    data = {row['k']: row['v'] for row in rows}
    if not rows or len(data) != len(rows):
        raise ValueError('Empty recording or duplicate keys: ' + str(path))
    return data


def differences(a, b, path='', exact=False):
    if isinstance(a, dict) and isinstance(b, dict):
        for key in sorted(a.keys() | b.keys()):
            if key not in a or key not in b:
                yield dict(path=path+'/'+key, expected=a.get(key, 'MISSING'), actual=b.get(key, 'MISSING'))
            else:
                yield from differences(a[key], b[key], path+'/'+key, exact or key in ('seed', 'count', 'read'))
    elif isinstance(a, list) and isinstance(b, list):
        for i in range(max(len(a), len(b))):
            if i >= len(a) or i >= len(b):
                yield dict(path=path+'/'+str(i), expected=a[i] if i < len(a) else 'MISSING', actual=b[i] if i < len(b) else 'MISSING')
            else:
                yield from differences(a[i], b[i], path+'/'+str(i), exact)
    elif isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool):
        equal = (a == b if isinstance(a, int) and isinstance(b, int) else struct.pack('f', a) == struct.pack('f', b)) if exact else math.isclose(a, b, abs_tol=2e-4, rel_tol=2e-4)
        if not math.isfinite(a) or not math.isfinite(b) or not equal:
            yield dict(path=path, expected=a, actual=b)
    elif a != b:
        yield dict(path=path, expected=a, actual=b)


def report(reference, actual):
    failures = []
    for key in sorted(reference.keys() | actual.keys()):
        if key not in reference or key not in actual:
            failures.append(dict(key=key, path='', expected=reference.get(key, 'MISSING'), actual=actual.get(key, 'MISSING')))
        else:
            failures.extend(dict(key=key, **d) for d in differences(reference[key], actual[key],
                exact=key.endswith(('/input', '/inputAfter')) or '/Custom' in key))
    bad = {f['key'] for f in failures}
    return dict(reference_records=len(reference), actual_records=len(actual), union_records=len(reference.keys() | actual.keys()),
                passing_records=len(reference.keys() | actual.keys())-len(bad), failing_records=len(bad),
                failing_values=len(failures), failures=failures)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--unity-version', required=True)
    p.add_argument('--native-a', type=Path)
    p.add_argument('--native-b', type=Path)
    p.add_argument('--actual', type=Path)
    p.add_argument('--baseline', type=Path)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    try:
        cache_artifact_path(a.output)
    except ValueError as error:
        p.error(str(error))
    if bool(a.native_a) != bool(a.native_b):
        p.error('Provide both independent Native recordings')
    canonical = Path(__file__).parent/'Reference'/('unity-'+a.unity_version+'.jsonl')
    reference = a.native_a or canonical
    result = {}
    inputs = [reference]
    if a.native_a:
        inputs.append(a.native_b)
        result['exact_native_agreement'] = a.native_a.read_bytes() == a.native_b.read_bytes()
        result['native'] = report(load(a.native_a), load(a.native_b))
    if canonical.exists():
        inputs.append(canonical)
        result['canonical_agrees_exactly'] = canonical.read_bytes() == reference.read_bytes()
    if a.actual:
        inputs.append(a.actual)
        result['candidate'] = report(load(reference), load(a.actual))
        if a.baseline:
            inputs.append(a.baseline)
            result['baseline'] = report(load(reference), load(a.baseline))
            before = {(f['key'], f['path']) for f in result['baseline']['failures']}
            after = {(f['key'], f['path']) for f in result['candidate']['failures']}
            result['union'] = {kind: [list(x) for x in sorted(values)] for kind, values in
                               [('resolved', before-after), ('retained', before&after), ('introduced', after-before)]}
    result['sha256'] = {str(f): hashlib.sha256(f.read_bytes()).hexdigest() for f in inputs}
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(result, indent=2)+'\n')
    print({k: {x: y for x, y in v.items() if x != 'failures'} if isinstance(v, dict) and k != 'union'
           else {x: len(y) for x, y in v.items()} if k == 'union' else v for k, v in result.items() if k != 'sha256'})
    return int(not result.get('exact_native_agreement', True) or not result.get('canonical_agrees_exactly', True)
               or bool(result.get('candidate', {}).get('failures')))


if __name__ == '__main__':
    sys.exit(main())
