#!/usr/bin/env python3
"""Compare Native and managed diagnostic order, keeping full input records in reports.

Native repeats must first pass physics2ddynamics/trace.py compare, which permits
only bijective pointer renaming. Across runtimes, public input poses bind body
identities independently at each lifetime operation, including pointer reuse.
"""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--native', type=Path, required=True)
p.add_argument('--candidate', type=Path, required=True)
p.add_argument('--release', required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parent
try:
    cache_artifact_path(a.output)
except ValueError as error:
    p.error(str(error))
public = json.loads((root/'Reference'/('unity-'+a.release+'.json')).read_text())
traces = [[json.loads(line) for line in path.read_text().splitlines()] for path in [a.native, a.candidate]]
steps = []
start = 1
for name, values in public.items():
    connected = start + 1 + (name in ['active', 'simulated'])
    steps.append((name, values, start, connected))
    start += 4 + (name in ['active', 'simulated'])
reports = []
errors = []
covered = [set(), set()]
for name, values, start, connected in steps:
    mappings = [{}, {}]
    for step in range(start, connected + 3):
        groups = [[(i, r) for i, r in enumerate(rows) if r['step'] == step] for rows in traces]
        for side, group in enumerate(groups):
            covered[side].update(i for i, _ in group)
            prefix = 'created' if step < connected else 'input'
            if step in [start, connected]:
                inputs = {k.split('/')[1]: v for k, v in values.items() if k.startswith(prefix+'/') and k.endswith('/position.x')}
                for _, row in group:
                    for b in row['bodies']:
                        matches = [label for label, x in inputs.items() if abs(b['x'] - x) < .001]
                        if len(matches) != 1:
                            errors.append(dict(scenario=name, side=side, step=step,
                                               unbound_body=b, input_matches=matches))
                        else:
                            mappings[side][b['ptr']] = matches[0]
        def order(side, group):
            def body(b):
                if b['ptr'] not in mappings[side]:
                    errors.append(dict(scenario=name, side=side, step=step, unknown_body=b))
                return mappings[side].get(b['ptr'], 'unbound/'+b['ptr'])
            return [dict(bodies=[body(b) for b in r['bodies']], contacts=[dict(bodyA=body(c['bodyA']), bodyB=body(c['bodyB']), points=c['points']) for c in r['contacts']]) for _, r in group]
        orders = [order(side, group) for side, group in enumerate(groups)]
        result = dict(scenario=name, step=step, agreed=bool(groups[0]) and bool(groups[1]) and orders[0] == orders[1],
                      native_order=orders[0], candidate_order=orders[1],
                      native=[r for _, r in groups[0]], candidate=[r for _, r in groups[1]])
        reports.append(result)
        if not result['agreed']: errors.append(dict(scenario=name, step=step))
extras = [[r for i, r in enumerate(rows) if i not in covered[side]] for side, rows in enumerate(traces)]
result = dict(steps=len(reports), agreed=not errors and not any(extras), errors=errors,
              extra_native=extras[0], extra_candidate=extras[1], records=reports)
result['islands'] = [len(rows) for rows in traces]
result['seed_matches'] = sum(n['bodies'][0] == c['bodies'][0] for r in reports
                             for n, c in zip(r['native_order'], r['candidate_order'])
                             if n['bodies'] and c['bodies'])
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
print(len(reports), 'steps;', len(errors), 'order mismatches;', sum(map(len, extras)), 'extra records')
raise SystemExit(not result['agreed'])
