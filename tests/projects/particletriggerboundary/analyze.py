#!/usr/bin/env python3
"""Compare recorded sphere arithmetic predicates with positive-life trigger retention."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('trigger_verify', ROOT.parent/'particletrigger/verify.py')
trigger = importlib.util.module_from_spec(spec)
spec.loader.exec_module(trigger)
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--unity-version', required=True)
p.add_argument('--recording', type=Path)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
trigger.cache_artifact_path(a.output)
path = a.recording or ROOT/'Reference'/('unity-'+a.unity_version+'.jsonl')
rows = trigger.load(path)
cases, hypotheses = [], {}
for (key, phase), row in sorted(rows.items()):
    if not key.startswith('arithmetic/') or not key.endswith('/killOutside') or phase != 'input':
        continue
    twin = key.removesuffix('/killOutside') + '/ignoreOutside'
    before = rows[key, 'before']['value']
    after = rows[key, 'after']['value']
    ignore = rows[twin, 'after']['value']
    data = row['value']
    assert before['count'] == before['read'] == ignore['count'] == ignore['read'] == 1, key
    assert before['particles'][0]['life'] > 0 and ignore['particles'][0]['life'] > 0, key
    assert after['count'] == after['read'] and after['count'] in (0, 1), key
    assert ignore['particles'][0]['position'] == data['particle']['position'], key
    inside = after['count'] == 1
    disagreements = []
    for name, prediction in data['predicates'].items():
        hypothesis = hypotheses.setdefault(name, dict(matches=0, disagreements=[]))
        if prediction == inside:
            hypothesis['matches'] += 1
        else:
            hypothesis['disagreements'].append(key)
            disagreements.append(name)
    geometry = '/'.join(key.split('/')[:2])
    cases.append(dict(key=key, inside=inside, disagreements=disagreements,
                      geometry=rows[geometry, 'geometry'], input=row,
                      before=rows[key, 'before'], after=rows[key, 'after'],
                      ignore_after=rows[twin, 'after']))
assert len(cases) == 6 * 2 * 7, len(cases)
result = dict(unity_version=a.unity_version, recording=str(path),
              sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
              case_count=len(cases), hypotheses=hypotheses, cases=cases)
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
print(json.dumps(dict(cases=len(cases), hypotheses={
    name: dict(matches=h['matches'], disagreements=len(h['disagreements']))
    for name, h in hypotheses.items()})))
