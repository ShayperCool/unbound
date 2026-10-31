#!/usr/bin/env python3
"""Verify immutable evidence, including the rejected candidate's complete delta."""
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
EVIDENCE = ROOT / 'Evidence'
spec = importlib.util.spec_from_file_location(
    'query_compare', ROOT.parent / 'query2dparity/compare.py')
compare_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare_module)


def load(name):
    return json.loads((EVIDENCE / name).read_text())


def failing(report):
    return {(item['scenario'], item['key']) for item in report['failures']}


assert (EVIDENCE / 'native1.json').read_bytes() == (EVIDENCE / 'native2.json').read_bytes()
reference = load('native1.json')
baseline = compare_module.compare(reference, load('accepted-baseline.json'))
candidate = compare_module.compare(reference, load('query-candidate.json'))
candidate.update(resolved=sorted(failing(baseline) - failing(candidate)),
                 retained=sorted(failing(baseline) & failing(candidate)),
                 introduced=sorted(failing(candidate) - failing(baseline)))
# JSON round-trip normalizes the pair tuples without changing any compared value.
assert baseline == load('baseline-comparison.json')
assert json.loads(json.dumps(candidate)) == load('query-candidate-comparison.json')
assert baseline['records'] == candidate['records'] == 644
assert baseline['matched'] == 473 and candidate['matched'] == 588
assert len(candidate['resolved']) == 129
assert len(candidate['retained']) == 42
assert len(candidate['introduced']) == 14
assert load('summary.json')['strict_acceptance'] is False
for path, expected in load('file-manifest.json').items():
    assert hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == expected, path
print('Two agreeing Native captures verified: 644 observations each.')
print('Candidate remains rejected: 42 retained and 14 introduced failures.')
