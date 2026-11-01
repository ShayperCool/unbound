#!/usr/bin/env python3
"""Compare full death-timing states without changing the particle comparator tolerances."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path

spec = importlib.util.spec_from_file_location('trigger_lifetime_verify', ROOT.parent/'particletriggerlifetime/verify.py')
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-version', required=True)
    parser.add_argument('--profile', choices=['default', 'fixed-0.01'], default='default')
    parser.add_argument('--native-a', type=Path)
    parser.add_argument('--native-b', type=Path)
    parser.add_argument('--actual', type=Path)
    parser.add_argument('--baseline', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if bool(args.native_a) != bool(args.native_b):
        parser.error('Provide both independent Native recordings')
    if args.baseline and not args.actual:
        parser.error('--baseline requires --actual')
    try:
        cache_artifact_path(args.output)
    except ValueError as error:
        parser.error(str(error))
    canonical = ROOT/'Reference'/('unity-'+args.unity_version)/(args.profile+'.jsonl')
    reference = args.native_a or canonical
    expected = {(name, step) for name in ('stationary_natural_death', 'death_child', 'ordinary_child')
                for step in ('before', 'after1', 'after2')}
    ref = comparison.load(reference)
    paths = [reference]
    result = {'reference_keys': comparison.report({key: {} for key in expected}, {key: {} for key in ref})}
    if args.native_a:
        paths.append(args.native_b)
        result['exact_native_agreement'] = args.native_a.read_bytes() == args.native_b.read_bytes()
        result['native'] = comparison.report(ref, comparison.load(args.native_b))
    if canonical.exists():
        paths.append(canonical)
        result['canonical_agrees_exactly'] = canonical.read_bytes() == reference.read_bytes()
    if args.actual:
        paths.append(args.actual)
        result['candidate'] = comparison.report(ref, comparison.load(args.actual))
        if args.baseline:
            paths.append(args.baseline)
            result['baseline'] = comparison.report(ref, comparison.load(args.baseline))
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
    print({key: {k: v for k, v in value.items() if k != 'failures'} if isinstance(value, dict) else value
           for key, value in result.items() if key not in ('sha256', 'union', 'changed_retained')})
    return int(not result.get('exact_native_agreement', True) or not result.get('canonical_agrees_exactly', True)
               or bool(result['reference_keys']['failures']) or bool(result.get('candidate', {}).get('failures')))


if __name__ == '__main__':
    sys.exit(main())
