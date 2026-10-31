#!/usr/bin/env python3
"""Verify explicit candidate captures against versioned Unity reference suites."""
import argparse
import json
import math
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_root, run_root
from report import compare, changes, describe, load


def verify_suites(root, suites, environment, exact=(), prefixes=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--suite', choices=list(suites))
    parser.add_argument('--candidate', type=Path, help='Single suite candidate, including an independent Unity capture')
    parser.add_argument('--work', type=Path, help='Candidate directory; defaults to the work environment when set')
    parser.add_argument('--references-only', action='store_true', help='Validate canonical reference structure without candidate parity claims')
    parser.add_argument('--label', default='final')
    parser.add_argument('--baseline', type=Path, help='Optional single-suite baseline; fail on introduced observations')
    parser.add_argument('--baseline-work', type=Path, help='Optional baseline directory, with <suite>-baseline.json captures')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if (args.candidate or args.baseline) and not args.suite:
        parser.error('--candidate and --baseline require --suite')
    environments = (environment,) if isinstance(environment, str) else environment
    configured = next((os.environ[key] for key in environments if os.environ.get(key)), None)
    work = args.work or (Path(configured) if configured else None)
    references_only = args.references_only or (not args.candidate and work is None)
    if references_only and (args.baseline or args.baseline_work):
        parser.error('a baseline requires candidate inputs')
    report = {}
    passed = True
    for name in ([args.suite] if args.suite else suites):
        native = reference_root(root)/suites[name]
        ref = load(native)
        if references_only:
            assert isinstance(ref, dict) and ref, native
            assert all(isinstance(name, str) and isinstance(values, dict) and values for name, values in ref.items()), native
            for values in ref.values():
                for key, value in values.items():
                    assert isinstance(key, str) and isinstance(value, (str, int, float, bool, type(None))), (native, key)
                    assert not isinstance(value, float) or math.isfinite(value), (native, key)
            report[name] = dict(reference=str(native), scenarios=len(ref), records=sum(map(len, ref.values())), structure_valid=True)
            print(name, report[name]['records'], 'canonical observations; structure valid, no candidate supplied', flush=True)
            continue
        prefix = (prefixes or {}).get(name, name+'-')
        candidate = args.candidate or work/f'{prefix}{args.label}.json'
        result = compare(ref, load(candidate), 1e-3)
        baseline = args.baseline or (args.baseline_work/f'{prefix}baseline.json' if args.baseline_work else None)
        if baseline:
            before = compare(ref, load(baseline), 1e-3)
            changes(before, result)
            result['baseline'] = before
            passed &= not result['introduced']
        else:
            passed &= not result['failures']
        if name in exact:
            passed &= result['exact'] == result['records']
        if name in ['lifecycle', 'receivers', 'timing', 'order']:
            passed &= not any(r['key'].endswith(('/step', '/kind', '/phase', '/self', '/other', '/sleep', '/touching'))
                              for r in result['rounding'])
        report[name] = result
        describe(name, result)
    output = args.output or run_root(root, 'reports')/('references.json' if references_only else 'verify-'+args.label+'.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2)+'\n')
    print(output)
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(verify_suites(ROOT, {'timing':'timing.json', 'order':'order.json'}, 'TRIGGER_WORK'))
