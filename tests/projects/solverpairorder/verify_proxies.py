#!/usr/bin/env python3
"""Check numeric proxy IDs and the complete star graph against Native snapshots."""
import argparse
import itertools
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
public = json.loads((Path(__file__).resolve().parent/'Reference'/('unity-'+a.release)/'public.json').read_text())
native = [json.loads(line) for line in a.native.read_text().splitlines()]
candidate = [json.loads(line) for line in a.candidate.read_text().splitlines()]
snapshots = [r for r in native if r['event'] == 'island']
errors, records = [], []
expected = []
for index, (scenario, values) in enumerate(public.items()):
    phases = ['separated', 'first' if scenario.startswith('staged-') else 'barrier',
              'both', 'removed', 'restored', 'final']
    for phase_index, phase in enumerate(phases):
        step = index*6 + phase_index + 1
        group = [r for r in snapshots if r['step'] == step]
        if len(group) != 1:
            errors.append(dict(step=step, native_snapshot_count=len(group), records=group))
            continue
        row = group[0]
        poses = {k.split('/')[1]: (v, values[k[:-1]+'y']) for k, v in values.items()
                 if k.startswith(phase+'/') and k.endswith('/position.x')}
        identities, bodies = {}, {}
        for proxy in row['proxies']:
            body = proxy['body']
            matches = [name for name, (x, y) in poses.items()
                       if abs(body['x']-x) < .001 and abs(body['y']-y) < .001]
            if len(matches) != 1:
                errors.append(dict(step=step, unknown_proxy=proxy, matches=matches))
                continue
            name = matches[0]
            identities[body['ptr']] = name
            bodies.setdefault(name, dict(name=name, proxyIDs=[], neighbours=[]))['proxyIDs'].append(proxy['proxyID'])
        if set(bodies) != set(poses): errors.append(dict(step=step, native_names=list(bodies), input_names=list(poses)))
        graphs = {identities[b['body']['ptr']]: [identities[e['other']] for e in b['edges']]
                  for b in row['bodies']}
        # Every neighbour is static, and static-static pairs cannot create edges.
        # Disconnected static bodies have no edges; connected ones have only H.
        star = {name: ['H'] if name in graphs['H'] else [] for name in poses if name != 'H'}
        star['H'] = graphs['H']
        for name, neighbours in graphs.items():
            if neighbours != star[name]: errors.append(dict(step=step, name=name, graph=graphs, star=star))
        for name in bodies: bodies[name]['neighbours'] = star[name]
        expected.append(dict(step=step, scenario=scenario, bodies=bodies))
for i, (n, c) in enumerate(itertools.zip_longest(expected, candidate), 1):
    actual = None
    if c is not None:
        actual = dict(c, bodies={b['name']: b for b in c['bodies']})
        if len(actual['bodies']) != len(c['bodies']): errors.append(dict(record=i, duplicate_names=c))
    agreed = n is not None and actual is not None and n == actual
    records.append(dict(record=i, agreed=agreed, native=n, candidate=actual, raw_candidate=c))
    if not agreed: errors.append(dict(record=i))
extras = [r for r in snapshots if not 1 <= r['step'] <= 6*len(public)]
result = dict(agreed=not errors and not extras, snapshots=[len(expected), len(candidate)],
              records=records, errors=errors, extra_native=extras)
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n')
print(result['snapshots'], len(errors), 'mismatches;', len(extras), 'extra snapshots')
raise SystemExit(not result['agreed'])
