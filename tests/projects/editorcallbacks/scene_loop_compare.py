#!/usr/bin/env python3
"""Compare every scene-loop record; report inter-object callback order separately.

Unity specifies no order between instances of the same MonoBehaviour class. The raw
trace remains ordered and every raw delta is printed. Each receiver's full callback
sequence, including frames/phases/state/counts, and the driver's full sequence
(including ordered root-query answers) are strict. No event is discarded.
"""
import argparse
from collections import defaultdict
import difflib
from pathlib import Path

CALLBACKS = {'Awake', 'OnEnable', 'Start', 'OnRenderObject'}


def streams(lines):
    result = defaultdict(list)
    for line in lines:
        fields = line.split('|')
        if len(fields) >= 7 and fields[3] in CALLBACKS:
            # Scene names and receiver names are authored labels. Repeated loads with
            # the same labels remain in one ordered stream, keeping every occurrence.
            key = (fields[0], fields[4], fields[5])
        else:
            key = ('driver',)
        result[key].append(line)
    return dict(result)


def compare(reference, actual):
    raw_matches = sum(b.size for b in difflib.SequenceMatcher(None, reference, actual, autojunk=False).get_matching_blocks())
    print('\n'.join(difflib.unified_diff(reference, actual, fromfile='reference', tofile='actual')))
    print(f'raw: {raw_matches}/{len(reference)} records match; {len(actual)} actual records')
    left, right = streams(reference), streams(actual)
    failures = 0
    for key in sorted(left.keys() | right.keys()):
        a, b = left.get(key, []), right.get(key, [])
        if a != b:
            failures += 1
            print('stream differs:', key)
            print('\n'.join(difflib.unified_diff(a, b, fromfile='reference stream', tofile='actual stream')))
    total = len(left.keys() | right.keys())
    print(f'{total - failures}/{total} receiver/driver streams match; every record compared')
    if not failures and reference != actual:
        print('Deferred: inter-object callback order differs; Unity documents no order between these script instances.')
    return int(failures != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('actual', type=Path)
    args = parser.parse_args()
    return compare(args.reference.read_text().splitlines(), args.actual.read_text().splitlines())


if __name__ == '__main__':
    raise SystemExit(main())
