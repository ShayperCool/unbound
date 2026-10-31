#!/usr/bin/env python3
"""Compare every sample, callback and value against the selected Native release."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'uitransitioncallbacks'))
from compare import compare
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--candidate', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
try:
    cache_artifact_path(a.output)
except ValueError as error:
    p.error(str(error))
report = compare(Path(__file__).resolve().parent / 'Reference' / f'unity-{a.release}.jsonl', a.candidate)
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps({k: v for k, v in report.items() if k != 'failures'}))
raise SystemExit(int(report['failed'] != 0))
