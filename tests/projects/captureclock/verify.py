#!/usr/bin/env python3
"""Compare every first-frame clock field, including missing and extra values."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--profile', required=True, choices=['fps60', 'dsp48000', 'slow', 'fps60-half', 'dsp48000-scaled'])
p.add_argument('--candidate', required=True, type=Path)
p.add_argument('--before', type=Path)
p.add_argument('--report', required=True, type=Path)
a = p.parse_args()
try:
    cache_artifact_path(a.report)
except ValueError as error:
    p.error(str(error))


def load(path):
    result = {}
    for row in map(json.loads, path.read_text().splitlines()):
        for field, value in row.items():
            key = str(row['frame']) + '/' + field
            if key in result:
                raise ValueError('Duplicate capture key: ' + key)
            result[key] = value
    return result


reference = load(Path(__file__).parent / 'Reference' / ('unity-' + a.release) / (a.profile + '.jsonl'))
candidate = load(a.candidate)
before = load(a.before) if a.before else None
rows = []
for key in sorted(reference.keys() | candidate.keys() | (before.keys() if before else set())):
    passed = key in reference and key in candidate and reference[key] == candidate[key]
    row = dict(key=key, reference=reference.get(key), candidate=candidate.get(key),
               missing=key not in candidate, extra=key not in reference, passed=passed)
    if before is not None:
        old_passed = key in reference and key in before and reference[key] == before[key]
        row.update(before=before.get(key), introduced=old_passed and not passed,
                   resolved=passed and not old_passed, retained=not passed and not old_passed)
    rows.append(row)
report = dict(observations=len(rows), matched=sum(row['passed'] for row in rows), values=rows)
a.report.parent.mkdir(parents=True, exist_ok=True)
a.report.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps({key: report[key] for key in ['observations', 'matched']}))
raise SystemExit(int(report['observations'] != report['matched']))
