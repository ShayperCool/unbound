#!/usr/bin/env python3
"""Record every changed float bit pattern, including changes inside the fixed tolerances."""
import argparse
import gzip
import json
import struct
from pathlib import Path
from compare import ATOL, RTOL, header

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--rate', required=True, type=int, choices=(44100, 48000))
p.add_argument('--candidate', required=True, type=Path)
p.add_argument('--report', required=True, type=Path)
a = p.parse_args()
reference = Path(__file__).parent/'Reference'/('unity-'+a.release)/('rate-'+str(a.rate))
left, right = header(reference), header(a.candidate)
assert left == right, 'Metadata must pass before recording numerical deltas'
summary = {}
with gzip.open(a.report, 'xt') as out:
    for key, meta in left.items():
        if not isinstance(meta, dict) or 'file' not in meta: continue
        x, y = [(root/meta['file']).read_bytes() for root in (reference, a.candidate)]
        assert len(x) == len(y)
        changed = outside = 0
        max_absolute = 0.0
        for i in range(0, len(x), 4):
            xb, yb = x[i:i+4], y[i:i+4]
            if xb == yb: continue
            native, candidate = struct.unpack('<f', xb)[0], struct.unpack('<f', yb)[0]
            error = abs(native-candidate)
            passed = error <= ATOL+RTOL*abs(native)
            changed += 1; outside += not passed; max_absolute = max(max_absolute, error)
            out.write(json.dumps(dict(key=key, sample=i//4, native=native, candidate=candidate,
                                      native_bits=xb.hex(), candidate_bits=yb.hex(), within_tolerance=passed))+'\n')
        if changed: summary[key] = dict(changed=changed, outside_tolerance=outside, max_absolute=max_absolute)
    out.write(json.dumps(dict(summary=summary))+'\n')
a.report.with_suffix('').with_suffix('.summary.json').write_text(json.dumps(summary, indent=2)+'\n')
print(sum(row['changed'] for row in summary.values()), 'changed float patterns recorded')
