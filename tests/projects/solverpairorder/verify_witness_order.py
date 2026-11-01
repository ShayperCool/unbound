#!/usr/bin/env python3
"""Compare all 300 declared stack steps after preserving the original broad prefix."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--native-public', type=Path, required=True)
p.add_argument('--native-updates', type=Path, required=True)
p.add_argument('--native', type=Path, required=True)
p.add_argument('--candidate', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
cache_artifact_path(a.output)
public = json.loads(a.native_public.read_text())['original/stack']
poses = {key.split('/')[2]: (x, public[key[:-1]+'y']) for key, x in public.items()
         if key.startswith('s1/before/') and key.endswith('/position.x')}
traces = [[json.loads(line) for line in path.read_text().splitlines()] for path in [a.native, a.candidate]]
updates = [json.loads(line) for line in a.native_updates.read_text().splitlines()]
# The source-selected final case contains exactly 300 Simulate calls. Collide
# records continue even after all bodies sleep and no island is solved.
declared_anchor = max(row['step'] for row in updates)-299
errors, anchors, mappings = [], [], []
for side, rows in enumerate(traces):
    anchor = declared_anchor
    for step in [anchor]:
        bodies = [body for row in rows if row['step'] == step for body in row['bodies']]
        if not all(any(body['type'] == 2 and abs(body['x']-x) < .001 and abs(body['y']-y) < .001
                   for body in bodies) for x, y in poses.values()):
            raise SystemExit('Missing declared four-body stack input on side '+str(side))
    mapping = {}
    for row in rows:
        if row['step'] != anchor: continue
        for body in row['bodies']:
            # The source has one static ground. Native uses its default static
            # body at the origin; managed uses a body at the collider's pose.
            matches = ['ground'] if body['type'] == 0 else [name for name, (x, y) in poses.items()
                if abs(body['x']-x) < .001 and abs(body['y']-y) < .001]
            if len(matches) != 1: errors.append(dict(side=side, body=body, matches=matches))
            else: mapping[body['ptr']] = matches[0]
    if len(set(mapping.values())) != len(mapping): errors.append(dict(side=side, ambiguous_mapping=mapping))
    anchors.append(anchor); mappings.append(mapping)
records = []
for relative_step in range(300):
    groups = [[row for row in rows if row['step'] == anchors[side]+relative_step]
              for side, rows in enumerate(traces)]
    def order(side, group):
        def name(body):
            if body['ptr'] not in mappings[side]: errors.append(dict(side=side, step=relative_step+1, unknown_body=body))
            return mappings[side].get(body['ptr'], 'unbound/'+str(body['ptr']))
        return [dict(bodies=[dict(name=name(body), type=body['type']) for body in row['bodies']],
                     contacts=[dict(bodyA=name(c['bodyA']), bodyB=name(c['bodyB']), points=c['points'])
                               for c in row['contacts']]) for row in group]
    orders = [order(side, group) for side, group in enumerate(groups)]
    agreed = orders[0] == orders[1]
    records.append(dict(step=relative_step+1, agreed=agreed, native_order=orders[0], candidate_order=orders[1],
                        native=groups[0], candidate=groups[1]))
    if not agreed: errors.append(dict(step=relative_step+1))
extras = [[row for row in rows if row['step'] >= anchors[side]+300] for side, rows in enumerate(traces)]
result = dict(agreed=not errors and not any(extras), anchors=anchors, steps=300,
              islands=[sum(len(r[key]) for r in records) for key in ['native', 'candidate']],
              records=records, errors=errors, extra_native=extras[0], extra_candidate=extras[1],
              prefix_records=[sum(row['step'] < anchors[side] for row in rows) for side, rows in enumerate(traces)],
              inputs_sha256={str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest()
                             for path in [a.native_public, a.native_updates, a.native, a.candidate]})
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
print(result['islands'], len(errors), 'order mismatches;', sum(map(len, extras)), 'extra records')
raise SystemExit(not result['agreed'])
