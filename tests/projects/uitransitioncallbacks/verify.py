#!/usr/bin/env python3
"""Gate against a release oracle; optional baseline reports every changed failure key."""
import argparse
import json
from pathlib import Path
import sys
from compare import compare

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--candidate', type=Path, required=True)
p.add_argument('--before', type=Path)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
try:
    cache_artifact_path(a.output)
except ValueError as error:
    p.error(str(error))
reference = Path(__file__).resolve().parent/'Reference'/f'unity-{a.release}.jsonl'
report = compare(reference, a.candidate)
if a.before:
    before = compare(reference, a.before)
    old = {f['key'] for f in before['failures']}
    new = {f['key'] for f in report['failures']}
    report['changes'] = {'resolved': sorted(old-new), 'retained': sorted(old & new), 'introduced': sorted(new-old)}
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(report, indent=2)+'\n')
print(json.dumps({k:v for k,v in report.items() if k not in ('failures', 'changes')}))
raise SystemExit(int(report['failed'] != 0))
