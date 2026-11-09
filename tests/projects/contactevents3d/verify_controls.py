#!/usr/bin/env python3
"""Gate stable controls while retaining process-dependent hash observations in the raw report."""
import argparse
import json
import re
from pathlib import Path
from compare import read, compare
from artifact_paths import artifact_path

HASHES = frozenset(('scene/0/hash', 'scene/4/defaultHash'))


def verify(reference, candidate):
    result = compare(reference, candidate)
    failures = []
    for key in HASHES:
        value = candidate.get(key)
        if not isinstance(value, str) or not re.fullmatch(r'-?\d+', value) or not -(2**31) <= int(value) < 2**31:
            failures.append({'key': key, 'reason': 'missing or invalid signed hash', 'candidate': value})
    if candidate.get('scene/0/hash') != candidate.get('scene/4/defaultHash'):
        failures.append({'key': 'scene hashes', 'reason': 'equal PhysicsScene values have different hashes'})
    for diff in result['differences']:
        # Presence remains mandatory; only the literal hash changes between processes.
        if diff['key'] not in HASHES or diff.get('reason') == 'missing/extra':
            failures.append(diff)
    return result, failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output = artifact_path(args.output)
    result, failures = verify(read(args.reference), read(args.candidate))
    if args.output.exists():
        raise SystemExit('refusing overwrite '+str(args.output))
    args.output.write_text(json.dumps({'raw': result, 'failures': failures}, indent=2)+'\n')
    print(f"controls: {result['records']} observations, {len(result['differences'])} raw differences, {len(failures)} failures")
    for failure in failures:
        print(json.dumps(failure))
    return bool(failures)


if __name__ == '__main__':
    raise SystemExit(main())
