#!/usr/bin/env python3
"""Report every birth residual and reconstruct the ballistic event coordinates."""
import argparse
import hashlib
import json
from pathlib import Path

import verify


def coordinates(records):
    observations = []
    for key, row in sorted(records.items()):
        name, step = key
        if not name.startswith('phase_') or not name.endswith('_child'):
            continue
        parent_name = name[:-len('_child')]
        inputs = records[(parent_name + ' inputs', 'configured')]['value']
        parent = records[(parent_name, step)]
        parent_age = (parent['particles'][0]['startLife'] - parent['particles'][0]['life']
                      if parent['particles'] else None)
        particles = []
        # These short captures have one parent, constant ballistic velocities, no
        # shape/forces and no deaths. Buffer order therefore retains emission order.
        for index, particle in enumerate(row['particles']):
            age = particle['startLife'] - particle['life']
            event_age = inputs['childDelay'] + ((index + 1) / inputs['childRate']
                        if inputs['childRate'] > 0 else inputs['childBurst'])
            elapsed = parent_age - event_age if parent_age is not None else None
            particles.append(dict(index=index, particle=particle, age=age,
                                  ballistic_origin=[p - v * age for p, v in zip(particle['pos'], particle['vel'])],
                                  scheduled_parent_age=event_age,
                                  elapsed_after_scheduled_event=elapsed,
                                  age_minus_elapsed=age - elapsed if elapsed is not None else None))
        observations.append(dict(key=key, inputs=inputs, parent=parent, child=row,
                                 parent_age=parent_age, reconstructed_particles=particles))
    return observations


def failure_map(report):
    return {(tuple(row['key']), row['path']): row for row in report['failures']}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--unity-version', required=True)
    p.add_argument('--actual', type=Path, required=True)
    p.add_argument('--previous-reference', type=Path, required=True)
    p.add_argument('--previous-actual', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    verify.cache_artifact_path(a.output)
    native_path = verify.ROOT / 'Reference' / ('unity-' + a.unity_version + '.jsonl')
    native, actual = verify.load(native_path), verify.load(a.actual)
    previous_native, previous_actual = verify.load(a.previous_reference), verify.load(a.previous_actual)
    native_subset = {key: native[key] for key in previous_native if key in native}
    actual_subset = {key: actual[key] for key in previous_native if key in actual}
    before = verify.report(previous_native, previous_actual)
    after = verify.report(previous_native, actual_subset)
    bf, af = failure_map(before), failure_map(after)
    union = {kind: [dict(key=key, path=path, before=bf.get((key, path)), after=af.get((key, path)))
                    for key, path in sorted(keys)]
             for kind, keys in [('resolved', bf.keys() - af.keys()), ('retained', bf.keys() & af.keys()),
                                ('introduced', af.keys() - bf.keys())]}
    result = dict(full=verify.report(native, actual),
                  previous_native_preservation=verify.report(previous_native, native_subset),
                  previous_actual_exact=previous_actual == actual_subset,
                  previous_before=before, previous_after=after, previous_union=union,
                  changed_retained=[dict(before=bf[k], after=af[k]) for k in sorted(bf.keys() & af.keys()) if bf[k] != af[k]],
                  native_event_coordinates=coordinates(native), actual_event_coordinates=coordinates(actual),
                  interpretation='Ballistic origins and candidate event coordinates are derived observations. '
                                 'Age-minus-elapsed exposes the unresolved newborn chronology; it is not an age correction.',
                  sha256={str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                          for path in [native_path, a.actual, a.previous_reference, a.previous_actual]})
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(result, indent=2) + '\n')
    print(dict(full={k: v for k, v in result['full'].items() if k != 'failures'},
               previous_actual_exact=result['previous_actual_exact'],
               previous_union={k: len(v) for k, v in union.items()},
               changed_retained=len(result['changed_retained'])))
    return int(bool(result['previous_native_preservation']['failures']) or not result['previous_actual_exact']
               or bool(union['introduced']))


if __name__ == '__main__':
    raise SystemExit(main())
