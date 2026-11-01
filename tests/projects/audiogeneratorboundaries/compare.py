#!/usr/bin/env python3
"""Use the original complete audio union and unchanged sample tolerances."""
import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'mixersuspend'))
from compare import compare, header

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--reference', type=Path)
p.add_argument('--candidate', type=Path, required=True)
p.add_argument('--before', type=Path)
p.add_argument('--report', type=Path, required=True)
p.add_argument('--exact', action='store_true')
a = p.parse_args()
assert a.report.name.endswith('.jsonl.gz')
reference = a.reference or Path(__file__).parent / 'Reference' / ('unity-' + a.release)
assert header(reference)['release'] == a.release and header(a.candidate)['release'] == a.release
raise SystemExit(bool(compare(reference, a.candidate, a.report, a.exact, a.before, True)))
