#!/usr/bin/env python3
"""Union-key comparison using the unchanged broad geometry/motion rule."""
import json
import math
import sys
from pathlib import Path

def compare(reference, candidate):
    failures, rounding = [], []
    records = exact = 0
    for scenario in sorted(reference.keys() | candidate.keys()):
        a, b = reference.get(scenario, {}), candidate.get(scenario, {})
        for key in sorted(a.keys() | b.keys()):
            x, y = a.get(key, '<missing>'), b.get(key, '<missing>')
            records += 1
            if x == y:
                exact += 1
                continue
            item = dict(scenario=scenario, key=key, reference=x, candidate=y)
            numeric = isinstance(x, (int, float)) and isinstance(y, (int, float)) and not isinstance(x, bool) and not isinstance(y, bool)
            if numeric and math.isfinite(x) and math.isfinite(y) and abs(x-y) <= 1e-3 * max(1, abs(x)):
                rounding.append(item)
            else:
                failures.append(item)
    return dict(records=records, exact=exact, matched=records-len(failures), failures=failures, passing_rounding=rounding)

if __name__ == '__main__':
    result=compare(json.loads(Path(sys.argv[1]).read_text()),json.loads(Path(sys.argv[2]).read_text()))
    Path(sys.argv[3]).write_text(json.dumps(result,indent=2)+'\n')
    print({k:v if isinstance(v,int) else len(v) for k,v in result.items()})
