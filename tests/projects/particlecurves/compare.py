#!/usr/bin/env python3
"""Compare every key and value of a particlecurves capture with the reference.

Values agree within 0.0002 absolute plus 0.0002 relative (the particle probes' bound); mode,
count and sentinel values are compared by the same rule. A missing or extra key, a different
length or any differing value fails, and every failure is printed with both raw records.

One measured difference outside this probe's subject is reported as known, not failing: in
`sim/<n>/c` Unity emits a burst whose time equals the duration while the step crosses the loop
end, and Unbound does not. The classifier accepts exactly that: the reference's last
`BURST_AT_DURATION` particles missing, those being the record's youngest (largest remaining
lifetime), with the count and every other particle matching.
"""
import json
import re
import sys

TOLERANCE = 2e-4
PARTICLE_FIELDS = 6
REMAINING = 3  # remaining lifetime within a particle's fields
BURST_AT_DURATION = 2
KNOWN = re.compile(r'^sim/\d/c$')


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


def near(x, y):
    return abs(x - y) <= TOLERANCE + TOLERANCE * abs(x)


def burst_at_duration_missing(key, r, c):
    """The reference with its last burst-at-duration particles removed equals the candidate."""
    if not KNOWN.match(key) or not r or not c or r[0] - c[0] != BURST_AT_DURATION:
        return False
    kept = r[1:1 + int(c[0]) * PARTICLE_FIELDS]
    dropped = r[1 + len(kept):]
    if len(c) != 1 + len(kept) or len(r) != 1 + int(r[0]) * PARTICLE_FIELDS:
        return False
    oldest_dropped = min(dropped[REMAINING::PARTICLE_FIELDS])
    youngest_kept = max(kept[REMAINING::PARTICLE_FIELDS], default=float('-inf'))
    return oldest_dropped > youngest_kept and all(near(x, y) for x, y in zip(kept, c[1:]))


def compare(reference, candidate):
    """Returns (passing keys, known keys, failures) where a failure is (key, reason, reference, candidate)."""
    failures = []
    known = []
    passing = 0
    for key in sorted(set(reference) | set(candidate)):
        r, c = reference.get(key), candidate.get(key)
        if r is not None and c is not None and burst_at_duration_missing(key, r, c):
            known.append((key, 'burst at time == duration not emitted', r, c))
        elif r is None or c is None:
            failures.append((key, 'extra' if r is None else 'missing', r, c))
        elif len(r) != len(c):
            failures.append((key, 'length %d != %d' % (len(r), len(c)), r, c))
        else:
            bad = [i for i, (x, y) in enumerate(zip(r, c)) if not near(x, y)]
            if bad:
                failures.append((key, 'values at ' + ','.join(map(str, bad)), r, c))
            else:
                passing += 1
    return passing, known, failures


def main():
    if len(sys.argv) != 3:
        raise SystemExit('usage: compare.py REFERENCE CANDIDATE')
    reference, candidate = read(sys.argv[1]), read(sys.argv[2])
    passing, known, failures = compare(reference, candidate)
    for label, rows in (('KNOWN', known), ('FAIL', failures)):
        for key, reason, r, c in rows:
            print(label, key, reason)
            print('  reference', r)
            print('  candidate', c)
    total = len(set(reference) | set(candidate))
    print('%d/%d records match; %d known difference(s); %d failing' % (passing, total, len(known), len(failures)))
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
