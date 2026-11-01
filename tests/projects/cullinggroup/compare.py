#!/usr/bin/env python3
"""Compares two culling-group records line by line, in order: the order of the records is the
measurement (which callbacks come between which frame events), so every record must match
exactly. Every missing (reference only) and extra (candidate only) record is reported.

    compare.py REFERENCE CANDIDATE
    compare.py --native A B        two Editor captures must be byte-identical
"""
import difflib
import json
import sys


def lines(path):
    rows = open(path).read().splitlines()
    for row in rows:
        json.loads(row)
    return rows


def main(argv):
    native = argv[:1] == ['--native']
    if native:
        argv = argv[1:]
    if len(argv) != 2:
        sys.exit(__doc__)
    reference, candidate = (lines(path) for path in argv)
    missing = extra = 0
    for group in difflib.SequenceMatcher(None, reference, candidate, autojunk=False).get_grouped_opcodes(2):
        for tag, i1, i2, j1, j2 in group:
            if tag == 'equal':
                for row in reference[i1:i2]:
                    print('  ', row)
                continue
            for row in reference[i1:i2]:
                print('- ', row)
                missing += 1
            for row in candidate[j1:j2]:
                print('+ ', row)
                extra += 1
        print('--')
    matched = len(reference) - missing
    print(f'records: reference {len(reference)}, candidate {len(candidate)}, matched {matched}, '
          f'missing {missing}, extra {extra}: {"PASS" if missing == extra == 0 else "FAIL"}')
    return 0 if missing == extra == 0 else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
