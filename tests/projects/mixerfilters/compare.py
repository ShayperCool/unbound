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
    failures = []
    def manifest(root):
        path = root / 'capture.json'
        if not path.exists():
            failures.append(dict(missing_capture=str(path)))
            return {}
        return json.loads(path.read_text())
    left, right = manifest(reference), manifest(candidate)
    tested = 0
    for key in sorted(left.keys() | right.keys()):
        a, b = left.get(key), right.get(key)
        if key not in left or key not in right or a != b:
            failures.append(dict(key=key, reference=a, candidate=b))
        if not any(isinstance(meta, dict) and 'file' in meta for meta in (a, b)):
            tested += 1
            continue
        samples = []
        for root, meta in ((reference, a), (candidate, b)):
            values = array.array('f')
            if isinstance(meta, dict) and 'file' in meta:
                path = root / meta['file']
                if path.exists():
                    values.frombytes(path.read_bytes())
                else:
                    failures.append(dict(key=key, missing_file=str(path)))
            samples.append(values)
        x, y = samples
        for i in range(max(len(x), len(y))):
            tested += 1
            av = x[i] if i < len(x) else None
            bv = y[i] if i < len(y) else None
            if av is None or bv is None or not math.isfinite(av) or not math.isfinite(bv) or abs(av-bv) > (0 if exact else ATOL + RTOL * abs(av)):
                failures.append(dict(key=key, sample=i,
                                     reference=repr(av) if av is not None and not math.isfinite(av) else av,
                                     candidate=repr(bv) if bv is not None and not math.isfinite(bv) else bv))
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
