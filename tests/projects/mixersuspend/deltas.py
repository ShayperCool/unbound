#!/usr/bin/env python3
"""Retain every changed float bit, including tolerated values and missing suffixes."""
import argparse
import gzip
import json
import math
from pathlib import Path
import struct
from compare import ATOL, RTOL, MISSING, header, raw

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--rate', required=True, type=int, choices=(44100, 48000))
p.add_argument('--profile', choices=('suspension', 'submission', 'lifecycle'), default='suspension')
p.add_argument('--reference', type=Path)
p.add_argument('--candidate', required=True, type=Path)
p.add_argument('--report', required=True, type=Path)
a = p.parse_args()
prefix = '' if a.profile == 'suspension' else a.profile+'-'
reference = a.reference or Path(__file__).parent/'Reference'/('unity-'+a.release)/(prefix+'rate-'+str(a.rate))
left, right = header(reference), header(a.candidate)
assert left['release'] == a.release and left['rate'] == a.rate
summary = {}
with gzip.open(a.report, 'xt', compresslevel=1) as out:
    for key in sorted(left.keys() | right.keys()):
        lm, rm = left.get(key, MISSING), right.get(key, MISSING)
        if lm != rm:
            out.write(json.dumps(dict(kind='metadata', key=key, reference=lm, candidate=rm))+'\n')
        file = next((m['file'] for m in (lm, rm) if isinstance(m, dict) and 'file' in m), None)
        if file is None:
            continue
        x, y = [raw(root, {'file': file}) for root in (reference, a.candidate)]
        assert len(x) % 4 == len(y) % 4 == 0, key
        changed = outside = missing = extra = 0
        maximum = 0.0
        for start in range(0, max(len(x), len(y)), 32768):
            if x[start:start+32768] == y[start:start+32768]:
                continue
            for i in range(start, min(start+32768, max(len(x), len(y))), 4):
                xb, yb = x[i:i+4], y[i:i+4]
                if xb == yb:
                    continue
                native = struct.unpack('<f', xb)[0] if xb else MISSING
                candidate = struct.unpack('<f', yb)[0] if yb else MISSING
                finite = bool(xb and yb) and math.isfinite(native) and math.isfinite(candidate)
                error = abs(native-candidate) if finite else None
                passed = finite and error <= ATOL+RTOL*abs(native)
                changed += 1
                outside += not passed
                missing += not bool(yb)
                extra += not bool(xb)
                if error is not None:
                    maximum = max(maximum, error)
                out.write(json.dumps(dict(key=key, sample=i//4, native=native, candidate=candidate,
                                          native_bits=xb.hex() if xb else MISSING,
                                          candidate_bits=yb.hex() if yb else MISSING,
                                          within_tolerance=passed))+'\n')
        if changed:
            summary[key] = dict(changed=changed, outside_tolerance=outside, missing=missing,
                                extra=extra, max_absolute=maximum)
    out.write(json.dumps(dict(summary=summary))+'\n')
a.report.with_suffix('').with_suffix('.summary.json').write_text(json.dumps(summary, indent=2)+'\n')
print(sum(row['changed'] for row in summary.values()), 'changed or missing float patterns recorded')
