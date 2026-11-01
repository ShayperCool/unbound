#!/usr/bin/env python3
"""Retain every observation and raw error change beside the original comparison."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path


def numeric(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def union(reference, baseline, candidate):
    rows = []
    for scenario in sorted(reference.keys() | baseline.keys() | candidate.keys()):
        sources = [data.get(scenario, {}) for data in (reference, baseline, candidate)]
        for key in sorted(set().union(*(data.keys() for data in sources))):
            row = dict(scenario=scenario, key=key)
            for label, data in zip(('reference', 'baseline', 'candidate'), sources):
                row[label + '_present'] = key in data
                if key in data:
                    row[label] = data[key]
            row['changed'] = (row['baseline_present'] != row['candidate_present'] or
                              row.get('baseline') != row.get('candidate'))
            if all(key in data and numeric(data[key]) for data in sources):
                expected, before, after = (data[key] for data in sources)
                row.update(baseline_error=abs(before - expected), candidate_error=abs(after - expected),
                           delta=after - before, baseline_delta=before - expected,
                           candidate_delta=after - expected)
                row['error_increased'] = row['candidate_error'] > row['baseline_error']
            rows.append(row)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path, required=True)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--candidate', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    cache_artifact_path(args.output)
    paths = [args.reference, args.baseline, args.candidate]
    rows = union(*(json.loads(path.read_text()) for path in paths))
    summary = dict(records=len(rows), changed=sum(row['changed'] for row in rows),
                   error_increases=sum(row.get('error_increased', False) for row in rows),
                   missing_reference_fields=sum(row['reference_present'] and not row['candidate_present'] for row in rows),
                   extra_candidate_fields=sum(row['candidate_present'] and not row['reference_present'] for row in rows))
    result = dict(summary=summary, rows=rows, inputs_sha256={
        str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open('x') as stream:
        stream.write(json.dumps(result, indent=2) + '\n')
    print(json.dumps(summary))


if __name__ == '__main__':
    main()
