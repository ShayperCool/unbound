#!/usr/bin/env python3
"""Compare every island input, binding identities to the probe's explicit poses."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--native', type=Path, required=True)
p.add_argument('--candidate', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
cache_artifact_path(a.output)
reference = Path(__file__).resolve().parent/'Reference'/('unity-'+a.release)
public = json.loads((reference/'public.json').read_text())
rows = [[json.loads(line) for line in f.read_text().splitlines()] for f in [a.native, a.candidate]]
errors, records = [], []
for index, (scenario, values) in enumerate(public.items()):
    phases = ['separated', 'first' if scenario.startswith('staged-') else 'barrier',
              'both', 'removed', 'restored', 'final']
    for phase_index, phase in enumerate(phases):
        step = index*6 + phase_index + 1
        poses = {k.split('/')[1]: (v, values[k[:-1]+'y']) for k, v in values.items()
                 if k.startswith(phase+'/') and k.endswith('/position.x')}
        groups = [[r for r in side if r['step'] == step] for side in rows]
        def order(group, side):
            def label(b):
                matches = [name for name, (x, y) in poses.items()
                           if abs(b['x']-x) < .001 and abs(b['y']-y) < .001]
                if len(matches) != 1:
                    errors.append(dict(scenario=scenario, phase=phase, side=side, body=b, matches=matches))
                    return 'unbound/'+str(b['ptr'])
                return matches[0]
            return [dict(bodies=[dict(name=label(b), type=b['type']) for b in r['bodies']],
                         contacts=[dict(bodyA=label(c['bodyA']), bodyB=label(c['bodyB']), points=c['points'])
                                   for c in r['contacts']]) for r in group]
        orders = [order(g, side) for side, g in enumerate(groups)]
        agreed = bool(groups[0]) and bool(groups[1]) and orders[0] == orders[1]
        records.append(dict(scenario=scenario, phase=phase, step=step, agreed=agreed,
                            native_order=orders[0], candidate_order=orders[1],
                            native=groups[0], candidate=groups[1]))
        if not agreed: errors.append(dict(scenario=scenario, phase=phase, step=step))
extras = [[r for r in side if not 1 <= r['step'] <= 6*len(public)] for side in rows]
result = dict(agreed=not errors and not any(extras), records=records, errors=errors,
              islands=list(map(len, rows)), extra_native=extras[0], extra_candidate=extras[1])
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
print(result['islands'], len(errors), 'order mismatches;', sum(map(len, extras)), 'extra records')
raise SystemExit(not result['agreed'])
