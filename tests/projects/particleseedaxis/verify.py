#!/usr/bin/env python3
"""Verify exact Native publication and full parity without dropping residuals."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path

# Reuse the safe particle control's full-union comparator and original tolerances.
spec = importlib.util.spec_from_file_location('trigger_lifetime_verify', ROOT.parent/'particletriggerlifetime/verify.py')
compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare)


def structural(rows):
    expected = {(f'manual_{seed}_{flags}_{order}', step)
                for seed in [12345, 305419896] for flags in [0, 15]
                for order in [0, 1] for step in range(4)}
    expected |= {(f'emit_{seed}', step) for seed in [12345, 305419896] for step in range(4)}
    counts = {key: key[1] for key in expected}
    for name, steps in {
        'bulk2': [0, 2, 3], 'bulk5': [0, 5, 6], 'deferred_read': [0, 3, 4],
        'seed_reset': [None, 2, 2], 'size3d': [None, 2, 3],
        'rotation3d': [None, 2, 3], 'both3d': [None, 2, 3],
        'random_scalar': [None, 2, 3], 'random3d': [None, 2, 3],
        'explicit_seed': [None, 2, 3], 'import_axis': [None, 1, 2],
    }.items():
        counts.update({(name, step): n for step, n in enumerate(steps) if n is not None})
    failures = compare.report({key: {} for key in counts}, {key: {} for key in rows})['failures']
    for key, row in rows.items():
        for field, want in [('count', counts.get(key)), ('readCount', counts.get(key)), ('time', 0), ('parentCount', 0)]:
            if row.get(field) != want:
                failures.append(dict(key=key, path='/'+field, expected=want, actual=row.get(field, 'MISSING')))
    return failures


def exact_seed_union(reference, actual):
    # Raw uint equality is reported separately; float/color tolerances remain unchanged.
    failures = []
    for key in sorted(reference.keys() | actual.keys()):
        left = reference.get(key, {}).get('particles', [])
        right = actual.get(key, {}).get('particles', [])
        for i in range(max(len(left), len(right))):
            a = left[i].get('seed', 'MISSING') if i < len(left) else 'MISSING'
            b = right[i].get('seed', 'MISSING') if i < len(right) else 'MISSING'
            if a != b:
                failures.append(dict(key=key, path=f'/particles/{i}/seed', expected=a, actual=b))
    return failures


def strict_report(reference, actual):
    result = compare.report(reference, actual)
    existing = {(tuple(f['key']), f['path']) for f in result['failures']}
    result['failures'].extend(f for f in exact_seed_union(reference, actual)
                              if (tuple(f['key']), f['path']) not in existing)
    bad = {tuple(f['key']) for f in result['failures']}
    result.update(failing_values=len(result['failures']), failing_records=len(bad),
                  passing_records=result['union_records']-len(bad))
    return result


def full_union(before, after):
    left = {(tuple(f['key']), f['path']): f for f in before['failures']}
    right = {(tuple(f['key']), f['path']): f for f in after['failures']}
    return {kind: [dict(key=key, path=path, baseline=left.get((key, path)),
                       candidate=right.get((key, path))) for key, path in sorted(keys)]
            for kind, keys in [('resolved', left.keys()-right.keys()),
                               ('retained', left.keys() & right.keys()),
                               ('introduced', right.keys()-left.keys())]}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--unity-version', required=True)
    p.add_argument('--native-a', type=Path)
    p.add_argument('--native-b', type=Path)
    p.add_argument('--actual', type=Path)
    p.add_argument('--baseline', type=Path)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    if bool(a.native_a) != bool(a.native_b):
        p.error('Provide both independent recordings')
    cache_artifact_path(a.output)
    canonical = ROOT/'Reference'/('unity-'+a.unity_version+'.jsonl')
    source = a.native_a or canonical
    reference = compare.load(source)
    paths = [source]
    result = dict(structural_failures=structural(reference))
    if a.native_a:
        paths.append(a.native_b)
        other = compare.load(a.native_b)
        result.update(exact_native_agreement=a.native_a.read_bytes() == a.native_b.read_bytes(),
                      native=compare.report(reference, other), native_structural_failures=structural(other))
    if canonical.exists():
        paths.append(canonical)
        result['canonical_agrees_exactly'] = canonical.read_bytes() == source.read_bytes()
    if a.actual:
        paths.append(a.actual)
        actual = compare.load(a.actual)
        result.update(actual=strict_report(reference, actual), actual_structural_failures=structural(actual),
                      exact_seed_failures=exact_seed_union(reference, actual))
        if a.baseline:
            paths.append(a.baseline)
            baseline = compare.load(a.baseline)
            result['baseline'] = strict_report(reference, baseline)
            result['union'] = full_union(result['baseline'], result['actual'])
            original = lambda rows: {key: row for key, row in rows.items()
                                     if key[0].startswith(('manual_', 'emit_'))}
            result['original40'] = dict(baseline=strict_report(original(reference), original(baseline)),
                                        actual=strict_report(original(reference), original(actual)))
            result['original40']['union'] = full_union(result['original40']['baseline'], result['original40']['actual'])
    result['sha256'] = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(result, indent=2)+'\n')
    def summary(value):
        if isinstance(value, list):
            return len(value)
        if isinstance(value, dict):
            return {key: summary(item) for key, item in value.items() if key not in ('failures', 'sha256')}
        return value
    print(summary(result))
    return int(bool(result['structural_failures']) or not result.get('exact_native_agreement', True)
               or not result.get('canonical_agrees_exactly', True)
               or bool(result.get('native_structural_failures'))
               or bool(result.get('actual', {}).get('failures')) or bool(result.get('exact_seed_failures'))
               or bool(result.get('actual_structural_failures')))


if __name__ == '__main__':
    sys.exit(main())
