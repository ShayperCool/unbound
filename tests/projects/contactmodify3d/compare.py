#!/usr/bin/env python3
"""Compare two contact-modification recordings record by record.

Integers and strings (counts, names, flags, face indices) must be equal; floats must agree
within ABS + REL * |reference|. Every differing record is listed; records under a KNOWN prefix
are listed as known differences and do not fail the comparison.
"""
import json
import sys

ABS = 1e-4
REL = 1e-4

# Open gaps, still printed. hasModifiableContacts changed on a live contact of colliders without
# collision message receivers: Unity applies it from the next step; Unbound keeps the state the
# pair was created with (as Unity does when a receiver exists). docs/COMPATIBILITY_GAPS.md.
KNOWN = {
    'pair/unflaggedLaterQuiet/': 'flag change on a live contact without receivers',
    'pair/flaggedLaterQuiet/': 'flag change on a live contact without receivers',
}


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


def same(a, b):
    if a is None or b is None or len(a) != len(b):
        return False
    for x, y in zip(a, b):
        if isinstance(x, float) or isinstance(y, float):
            if isinstance(x, str) or isinstance(y, str) or abs(x - y) > ABS + REL * abs(x):
                return False
        elif x != y:
            return False
    return True


def main():
    a, b = map(read, sys.argv[1:3])
    keys = sorted(a.keys() | b.keys())
    differ = [key for key in keys if not same(a.get(key), b.get(key))]
    known = [key for key in differ if any(key.startswith(prefix) for prefix in KNOWN)]
    bad = [key for key in differ if key not in known]
    for key in known:
        reason = next(KNOWN[prefix] for prefix in KNOWN if key.startswith(prefix))
        print(f'{key}: known difference ({reason}): reference {a.get(key)}, candidate {b.get(key)}')
    for key in bad:
        print(f'{key}: reference {a.get(key)}, candidate {b.get(key)}')
    groups = {}
    for key in bad:
        groups[key.split('/')[0]] = groups.get(key.split('/')[0], 0) + 1
    print(f'{len(keys) - len(differ)}/{len(keys)} records match; {len(known)} known difference(s); {len(bad)} differ {groups}')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
