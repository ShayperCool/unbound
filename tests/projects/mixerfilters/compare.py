#!/usr/bin/env python3
"""Compare complete interleaved float32 captures, preserving the full key/sample union."""
import argparse
import array
import json
import math
from pathlib import Path

# Fixed before any engine candidate. No alignment, trimming, or skipped samples.
ATOL = 2e-5
RTOL = 2e-4


def compare(reference, candidate, report, exact=False):
    left = json.loads((reference / 'capture.json').read_text())
    right = json.loads((candidate / 'capture.json').read_text())
    failures = []
    tested = 0
    for key in sorted(left.keys() | right.keys()):
        if key not in left or key not in right:
            failures.append(dict(key=key, reference=left.get(key), candidate=right.get(key)))
            continue
        a, b = left[key], right[key]
        if a != b:
            failures.append(dict(key=key, reference=a, candidate=b))
        if not isinstance(a, dict) or 'file' not in a or not isinstance(b, dict) or 'file' not in b:
            tested += 1
            continue
        samples = []
        for root, meta in ((reference, a), (candidate, b)):
            values = array.array('f')
            path = root / meta['file']
            if path.exists():
                values.frombytes(path.read_bytes())
            samples.append(values)
        x, y = samples
        for i in range(max(len(x), len(y))):
            tested += 1
            av = x[i] if i < len(x) else None
            bv = y[i] if i < len(y) else None
            if av is None or bv is None or not math.isfinite(av) or not math.isfinite(bv) or abs(av-bv) > (0 if exact else ATOL + RTOL * abs(av)):
                failures.append(dict(key=key, sample=i, reference=av, candidate=bv))
    report.write_text(json.dumps(dict(tested=tested, failed=len(failures), exact=exact,
                                     atol=0 if exact else ATOL, rtol=0 if exact else RTOL,
                                     failures=failures), indent=2, allow_nan=False)+'\n')
    print(f'{tested} values, {len(failures)} failures; full report: {report}')
    return bool(failures)


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--release', required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--reference', type=Path, help='Only for independent Native repeat checks')
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--exact', action='store_true')
    args = p.parse_args()
    reference = args.reference or Path(__file__).parent / 'Reference' / ('unity-'+args.release)
    raise SystemExit(compare(reference, args.candidate, args.report, args.exact))
