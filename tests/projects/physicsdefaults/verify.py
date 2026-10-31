#!/usr/bin/env python3
"""Verify the project-defaults oracle or compare an explicit candidate recording."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file
from compare import read

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--record-reference', type=Path, nargs=2, metavar=('NATIVE1', 'NATIVE2'))
    args = parser.parse_args()
    reference = reference_file(ROOT, 'jsonl')
    if args.record_reference:
        first, second = args.record_reference
        assert read(str(first)) == read(str(second)), 'Native disagreement'
        assert len(read(str(first))) == 582
        reference.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first, reference)
    assert len(read(str(reference))) == 582, reference
    return subprocess.run([sys.executable, str(ROOT / 'compare.py'), str(reference),
                           str(args.candidate or reference)]).returncode


if __name__ == '__main__':
    sys.exit(main())
