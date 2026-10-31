#!/usr/bin/env python3
"""Verify loading oracle structure and optional capture directories without a build."""
import argparse
from pathlib import Path
import shutil
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root
from report import compare, read

ROOT = Path(__file__).resolve().parent
ORDERS = ['transform-first', 'rigidbody-first']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--captures', type=Path, help='directory of <order>-<label>.jsonl')
    parser.add_argument('--label', default='final')
    parser.add_argument('--record-reference', type=Path, help='explicitly install four agreeing <order>-native1/2 captures')
    parser.add_argument('--full', action='store_true', help='also run the historical stage build/artifact audit using LOADING_WORK')
    args = parser.parse_args()
    reference = reference_root(ROOT) / 'loading.jsonl'
    if args.record_reference:
        paths = [args.record_reference / f'{order}-native{i}.jsonl' for order in ORDERS for i in [1, 2]]
        assert all(read(p) == read(paths[0]) for p in paths), 'Native disagreement'
        assert len(read(paths[0])) == 4656
        reference.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(paths[0], reference)
    oracle = read(reference)
    assert len(oracle) == 4656, reference
    assert set(reference_root(ROOT).glob('*.jsonl')) == {reference}
    assert not compare(oracle, oracle)['failures']
    print(f'4656 canonical records verified in {reference}')
    failures = 0
    if args.captures:
        for order in ORDERS:
            path = args.captures / (order + '-' + args.label + '.jsonl')
            result = compare(oracle, read(path))
            failures += len(result['failures'])
            print(f"{order}: {result['exact']}/{result['records']} exact; {len(result['failures'])} differ")
            for row in result['failures']:
                print(f"  {row['key']}: reference {row['reference']}, candidate {row['candidate']}")
    if args.full:
        from stage_verify import main as full_audit
        full_audit()
    return bool(failures)


if __name__ == '__main__':
    sys.exit(main())
