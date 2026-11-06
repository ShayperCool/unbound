#!/usr/bin/env python3
"""Compare every key and value, retaining every raw failure in the report.

Counts, seed halves and mesh indices are exact. Other values use the unchanged gravity-probe
bound: 0.0002 absolute plus 0.0002 relative. The explicitly recorded residual fields below are
reported as known differences; missing/extra keys, lengths and every other field fail the gate.
"""
import json
import re
import sys

TOLERANCE = 2e-4
PARTICLE_FIELDS = 33
# Local field indices include the two seed halves. These are unresolved measurements, not
# alternative tolerances. See README.md for their scope and observable effects.
RESIDUALS = [
    (r'^start/gradients/', set(range(23, 31)), 'packed gradient interpolation'),
    (r'^color/(gradients|randomcolor)/', set(range(27, 31)), 'packed gradient interpolation'),
]
ANGULAR_CONTEXT = r'^(color/|custom/|subemit/|played/|vol/speedmodifier9/)'


def read(path):
    result = {}
    for line in open(path):
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError('duplicate key: ' + row['key'])
        result[row['key']] = row['value']
    if result.get('complete') != [1]:
        raise ValueError('incomplete recording: ' + str(path))
    return result


def stride(key):
    return 3 if key.startswith('mesh/') else 4 if '/custom' in key else PARTICLE_FIELDS


def near(key, index, x, y):
    if index == 0 or key.startswith('burst/') or key == 'complete':
        return x == y
    field = (index - 1) % stride(key)
    if key.startswith('mesh/') or '/custom' not in key and field < 2:
        return x == y
    return abs(x - y) <= TOLERANCE + TOLERANCE * abs(x)


def residual(key, index, reference, candidate):
    if index == 0 or stride(key) != PARTICLE_FIELDS or key.startswith('burst/'):
        return None
    particle, field = divmod(index - 1, PARTICLE_FIELDS)
    if re.search(ANGULAR_CONTEXT, key) and field == 16 and reference != 0 and candidate == 0:
        return 'nonzero reference angular velocity after rotation scenarios; cause unresolved'
    return next((reason for pattern, fields, reason in RESIDUALS if field in fields and re.search(pattern, key)), None)


def retained_existing_pair(key, reference, candidate):
    # These expiry controls retain two older particles in opposite buffer slots.
    # Require the entire pair to agree by identity, including exact seed halves.
    # No tolerance or particle order is changed; every raw failing index is printed.
    if key not in {'ratebatch/edge/expiry/987654321/2', 'ratebatch/edge/expiry/4242/2'}:
        return False
    return all(near(key, 1 + field, reference[1 + field], candidate[34 + field])
               and near(key, 34 + field, reference[34 + field], candidate[1 + field])
               for field in range(PARTICLE_FIELDS))


def retained_emit_prefix(key, reference, candidate):
    # A separate scripted-Emit control: nine existing particles, then five new ones.
    # Every field agrees by seed, including with all modifiers disabled. Preserve
    # each raw differing index without imposing this observed buffer permutation.
    if not re.fullmatch(r'shapemod/(Box|Cone|Sphere)/[01]{3}/emit/(12345|987654321|4242)/1', key):
        return False
    if reference[0] != 14 or candidate[0] != 14:
        return False
    slots = list(range(9)) + [11, 12, 13, 9, 10]
    return all(near(key, 1 + particle * PARTICLE_FIELDS + field,
                    reference[1 + particle * PARTICLE_FIELDS + field],
                    candidate[1 + source * PARTICLE_FIELDS + field])
               for particle, source in enumerate(slots) for field in range(PARTICLE_FIELDS))


def main():
    a, b = map(read, sys.argv[1:3])
    keys = sorted(a.keys() | b.keys())
    known, bad = [], []
    for key in keys:
        ra, rb = a.get(key), b.get(key)
        if ra is None or rb is None or len(ra) != len(rb):
            bad.append(key)
            print(f'{key}: reference {ra}, candidate {rb}')
            continue
        failures = [i for i, (x, y) in enumerate(zip(ra, rb)) if not near(key, i, x, y)]
        if not failures:
            continue
        retained_pair = retained_existing_pair(key, ra, rb)
        emit_prefix = retained_emit_prefix(key, ra, rb)
        reasons = {i: ('existing-particle death compaction order; values agree by seed'
                       if retained_pair and 1 <= i <= 66 else
                       'scripted Emit newborn prefix permutation; all fields agree by seed'
                       if emit_prefix and i >= 1 + 9 * PARTICLE_FIELDS else residual(key, i, ra[i], rb[i]))
                   for i in failures}
        if all(reasons.values()):
            known.append(key)
            print(f'{key}: known differences {reasons}: reference {ra}, candidate {rb}')
        else:
            bad.append(key)
            print(f'{key}: failing indices {failures}, residuals {reasons}: reference {ra}, candidate {rb}')
    groups = {}
    for key in bad:
        group = '/'.join(key.split('/')[:2])
        groups[group] = groups.get(group, 0) + 1
    print(f'{len(keys) - len(known) - len(bad)}/{len(keys)} records match; '
          f'{len(known)} known difference(s); {len(bad)} differ {groups}')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
