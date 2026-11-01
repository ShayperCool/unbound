#!/usr/bin/env python3
"""Explain the control's independent interventions without masking parity fields."""
import argparse
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path


def diagnose(path):
    rows = [json.loads(line) for line in path.read_text().splitlines()]
    calls = [r for r in rows if r['step'] > 0]
    events, missing = [], []
    for r in calls:
        slot = r['step'] - 1
        if slot >= len(r['particles']):
            missing.append(r)
            continue
        p = r['particles'][slot]
        events.append(dict(scenario=r['scenario'], step=r['step'], kind=r['kind'],
                           childSeed=r['childSeed'], flags=r['flags'], order=r['order'],
                           parentSeed=r['supplied']['seed'] if r['supplied'] else None,
                           particle=p))

    def groups(fields, value):
        result = {}
        for e in events:
            key = tuple(e[f] for f in fields)
            result.setdefault(key, []).append(dict(scenario=e['scenario'], step=e['step'],
                                                  value=e['particle'][value]))
        return [dict(inputs=dict(zip(fields, key)), observations=obs,
                     identical=all(x['value'] == obs[0]['value'] for x in obs))
                for key, obs in sorted(result.items(), key=lambda x: str(x[0]))]

    # Every member and its full raw value survives, even when an invariant fails.
    return dict(records=len(rows), emitting_calls=len(calls), observed_events=len(events),
                missing_events=missing, events=events,
                seed_by_kind_parent=groups(['kind', 'parentSeed'], 'seed'),
                seed_by_kind_child_call=groups(['kind', 'childSeed', 'step'], 'seed'),
                axis_by_kind=groups(['kind'], 'axis'),
                initial_age=[dict(scenario=e['scenario'], step=e['step'],
                                  value=e['particle']['startLife']-e['particle']['life']) for e in events],
                nonzero_clocks=[r for r in rows if r['time'] != 0],
                count_failures=[r for r in rows if r['count'] != r['step'] or r['readCount'] != r['step']])


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--unity-version', required=True)
    p.add_argument('--input', type=Path)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    cache_artifact_path(a.output)
    result = diagnose(a.input or ROOT/'Reference'/('unity-'+a.unity_version+'.jsonl'))
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(result, indent=2)+'\n')
    print({k: v for k, v in result.items() if k in ('records', 'emitting_calls', 'observed_events')})


if __name__ == '__main__':
    main()
