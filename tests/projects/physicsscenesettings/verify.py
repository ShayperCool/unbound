#!/usr/bin/env python3
"""Check canonical oracle structure or compare explicitly selected run captures."""
import argparse
from pathlib import Path
import shutil
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root
from compare import matches, read
from parent_probes import SOURCES
from run import ROOT, PROFILES

GROUPS = [('', list(PROFILES)),
          ('contacts-', ['default', 'pairs-kk', 'pairs-ks', 'pairs-all']),
          ('islands-', ['default', 'determinism', 'tgs', 'tgs-det'])]
FOCUSED_RECORDS = {'kinematic': 15, 'triggers': 1728, 'order': 276,
                   'component': 276, 'barrier': 227, 'flags': 1152,
                   'trigger-pairs': 1440, 'ignore-lifecycle': 1848,
                   'sleep': 20700, 'sleep-inactive': 1050,
                   'clone-placement': 160, 'transform-clone-placement': 12,
                   'inactive-sequences': 4668}
CONTINUATION_RECORDS = {'original': 276, 'body-first': 277, 'collider-first': 276,
                        'disable-first': 276, 'creation-original': 278}


def matrix_paths():
    for prefix, names in GROUPS:
        for name in names:
            yield prefix, name, reference_root(ROOT) / prefix.rstrip('-') / (name + '.jsonl')


def verify_oracles():
    """Validate source/schema coverage without build artifacts or historical captures."""
    count = 0
    expected = set()
    for prefix, name, path in matrix_paths():
        data = read(str(path))
        assert len(data) == (299 if prefix == 'contacts-' else 227), path
        assert all(matches(k, v, v) for k, v in data.items()), path
        count += len(data)
        expected.add(path)
    assert count == 5055
    assert FOCUSED_RECORDS.keys() == SOURCES.keys()
    for group, probes in [('focused', FOCUSED_RECORDS), ('continuation', CONTINUATION_RECORDS)]:
        for name, records in probes.items():
            path = reference_root(ROOT) / group / (name + '.jsonl')
            data = read(str(path))
            assert len(data) == records, (path, len(data), records)
            assert all(matches(k, v, v) for k, v in data.items()), path
            count += len(data)
            expected.add(path)
    assert set(reference_root(ROOT).rglob('*.jsonl')) == expected, 'unexpected or missing oracle file'
    return count


def compare_file(reference, candidate, exact=False):
    a, b = read(str(reference)), read(str(candidate))
    keys = a.keys() | b.keys()
    if exact:
        bad = sorted(k for k in keys if k not in a or k not in b or a[k] != b[k])
    else:
        bad = sorted(k for k in keys if not matches(k, a.get(k), b.get(k)))
    equal = sum(k in a and k in b and a[k] == b[k] for k in keys)
    print(f'{candidate}: {len(keys)-len(bad)}/{len(keys)} match; {equal} exact; {len(bad)} differ')
    for key in bad:
        print(f'  {key}: reference {a.get(key)}, candidate {b.get(key)}')
    return len(bad)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--matrix-output', type=Path, help='parent of final/, contacts-final/, islands-final/')
    parser.add_argument('--label', default='final', help='capture label, e.g. final or baseline; empty uses <name>.jsonl')
    parser.add_argument('--focused-output', type=Path, help='directory of <probe>-<label>.jsonl')
    parser.add_argument('--continuation-output', type=Path, help='directory of <control>-<label>.jsonl')
    parser.add_argument('--record-focused', type=Path, help='explicitly install agreeing <probe>-native1/2 captures')
    parser.add_argument('--record-continuation', type=Path, help='explicitly install agreeing <control>-native1/2 captures')
    args = parser.parse_args()
    for directory, group, probes in [(args.record_focused, 'focused', FOCUSED_RECORDS),
                                      (args.record_continuation, 'continuation', CONTINUATION_RECORDS)]:
        if directory is None:
            continue
        for name in probes:
            first, second = [directory / f'{name}-native{i}.jsonl' for i in [1, 2]]
            assert read(str(first)) == read(str(second)), 'Native disagreement: ' + name
            assert len(read(str(first))) == probes[name], name
            dest = reference_root(ROOT) / group / (name + '.jsonl')
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(first, dest)
    print(f'{verify_oracles()} canonical records verified in {reference_root(ROOT)}')
    failed = 0
    if args.matrix_output:
        for prefix, name, reference in matrix_paths():
            failed += compare_file(reference, args.matrix_output / (prefix + args.label) / (name + '.jsonl'))
    if args.focused_output:
        for name in FOCUSED_RECORDS:
            failed += compare_file(reference_root(ROOT) / 'focused' / (name + '.jsonl'),
                                   args.focused_output / (name + ('-' + args.label if args.label else '') + '.jsonl'),
                                   exact=name not in ['order', 'component', 'barrier'])
    if args.continuation_output:
        for name in CONTINUATION_RECORDS:
            failed += compare_file(reference_root(ROOT) / 'continuation' / (name + '.jsonl'),
                                   args.continuation_output / (name + ('-' + args.label if args.label else '') + '.jsonl'))
    return bool(failed)


if __name__ == '__main__':
    sys.exit(main())
