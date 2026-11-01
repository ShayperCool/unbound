#!/usr/bin/env python3
"""Keep full birth-control unions using the broad particle comparator's tolerances."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path

spec = importlib.util.spec_from_file_location('particles_compare', ROOT.parent/'particles/compare.py')
particles = importlib.util.module_from_spec(spec)
spec.loader.exec_module(particles)


def load(path):
    rows = [json.loads(line) for line in path.read_text().splitlines()]
    records = {(row['scenario'], row['step']): row for row in rows}
    if not rows or len(rows) != len(records):
        raise ValueError('Empty recording or duplicate keys: ' + str(path))
    return records


def differences(expected, actual, path='', color=False):
    if isinstance(expected, dict) and isinstance(actual, dict):
        for key in sorted(expected.keys() | actual.keys()):
            if key in particles.IGNORED or key in ('scenario', 'step'):
                continue
            if key not in expected or key not in actual:
                yield dict(path=path+'/'+key, expected=expected.get(key, 'MISSING'), actual=actual.get(key, 'MISSING'))
            else:
                yield from differences(expected[key], actual[key], path+'/'+key, key in ('color', 'startColor'))
    elif isinstance(expected, list) and isinstance(actual, list):
        for index in range(max(len(expected), len(actual))):
            if index >= len(expected) or index >= len(actual):
                yield dict(path=path+'/'+str(index), expected=expected[index] if index < len(expected) else 'MISSING',
                           actual=actual[index] if index < len(actual) else 'MISSING')
            else:
                yield from differences(expected[index], actual[index], path+'/'+str(index), color)
    elif not particles.close(expected, actual, color):
        yield dict(path=path, expected=expected, actual=actual)


def report(reference, actual):
    failures = []
    for key in sorted(reference.keys() | actual.keys()):
        if key not in reference or key not in actual:
            failures.append(dict(key=key, path='', expected=reference.get(key, 'MISSING'), actual=actual.get(key, 'MISSING')))
        else:
            failures.extend(dict(key=key, **d) for d in differences(reference[key], actual[key]))
    bad = {tuple(row['key']) for row in failures}
    return dict(reference_records=len(reference), actual_records=len(actual), union_records=len(reference.keys() | actual.keys()),
                passing_records=len(reference.keys() | actual.keys())-len(bad), failing_records=len(bad),
                failing_values=len(failures), failures=failures)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-version', required=True)
    parser.add_argument('--native-a', type=Path)
    parser.add_argument('--native-b', type=Path)
    parser.add_argument('--actual', type=Path)
    parser.add_argument('--baseline', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if bool(args.native_a) != bool(args.native_b):
        parser.error('Provide both independent Editor recordings')
    try:
        cache_artifact_path(args.output)
    except ValueError as error:
        parser.error(str(error))
    canonical = ROOT/'Reference'/('unity-'+args.unity_version+'.jsonl')
    reference = args.native_a or canonical
    ref = load(reference)
    paths = [reference]
    result = {}
    if args.native_a:
        paths.append(args.native_b)
        result['exact_native_agreement'] = args.native_a.read_bytes() == args.native_b.read_bytes()
        result['native'] = report(ref, load(args.native_b))
    if canonical.exists():
        paths.append(canonical)
        result['canonical_agrees_exactly'] = canonical.read_bytes() == reference.read_bytes()
    # Every pre-existing birth history remains checked against the broad release oracle.
    broad_path = ROOT.parent/'particles/Reference'/('unity-'+args.unity_version+'.jsonl')
    paths.append(broad_path)
    broad = load(broad_path)
    names = ('sub_emitters_birth', 'sub_emitters_birth_child')
    original = {key: value for key, value in broad.items() if key[0] in names or key[0] in tuple(name+' inside' for name in names)}
    focused = {key: value for key, value in ref.items() if key[0] in names or key[0] in tuple(name+' inside' for name in names)}
    result['original_controls'] = report(original, focused)
    if args.actual:
        paths.append(args.actual)
        result['candidate'] = report(ref, load(args.actual))
        if args.baseline:
            paths.append(args.baseline)
            result['baseline'] = report(ref, load(args.baseline))
            before = {(tuple(f['key']), f['path']): f for f in result['baseline']['failures']}
            after = {(tuple(f['key']), f['path']): f for f in result['candidate']['failures']}
            result['union'] = {kind: [list(key) for key in sorted(keys)] for kind, keys in
                               [('resolved', before.keys()-after.keys()), ('retained', before.keys() & after.keys()),
                                ('introduced', after.keys()-before.keys())]}
            result['changed_retained'] = [dict(baseline=before[key], candidate=after[key])
                                         for key in sorted(before.keys() & after.keys()) if before[key] != after[key]]
    result['sha256'] = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2)+'\n')
    print({key: {k: v for k, v in value.items() if k != 'failures'} if isinstance(value, dict) and key not in ('union', 'sha256')
           else {k: len(v) for k, v in value.items()} if key == 'union' else value
           for key, value in result.items() if key not in ('sha256', 'changed_retained')})
    return int(not result.get('exact_native_agreement', True) or not result.get('canonical_agrees_exactly', True)
               or bool(result['original_controls']['failures']) or bool(result.get('candidate', {}).get('failures')))


if __name__ == '__main__':
    sys.exit(main())
