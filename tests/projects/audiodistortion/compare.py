#!/usr/bin/env python3
"""Stream every failing sample of the Native/candidate (or before/after) comparison."""
import argparse
import gzip
import json
import math
from pathlib import Path
import struct
from common import FIXTURE, RESIDUALS, SUFFIXES, header, profile, samples

ATOL = 2e-5
RTOL = 2e-4
MISSING = '<missing>'


def bad(a, b, sample, exact, pcm_steps=0, residual=0):
    if not sample or not isinstance(a, float) or not isinstance(b, float): return a != b
    if not math.isfinite(a) or not math.isfinite(b): return True
    if exact: return struct.pack('<f', a) != struct.pack('<f', b)
    # Device PCM truncates master floats to steps of 1/32768, so floats agreeing within the
    # tolerance can still land one step apart; --pcm-steps admits that many steps.
    return abs(a-b) > max(ATOL+RTOL*abs(a), pcm_steps/32768, residual)


def safe(value):
    return repr(value) if isinstance(value, float) and not math.isfinite(value) else value


def compare(reference, candidate, report, keys, exact=False, before=None, pcm_steps=0, residuals=False):
    roots = [reference, candidate] if before is None else [reference, before, candidate]
    manifests = [header(root) for root in roots]
    limits = {k+s: frames for k, frames in keys.items() for s in SUFFIXES}
    fields = ['release', 'rate'] + (['captureDeltaTime'] if any('captureDeltaTime' in m for m in manifests) else []) + list(limits)
    fields += sorted({f for m in manifests for f in m if f.split('.')[0] in keys and f not in fields
                      and not any(f == k+s for k in keys for s in SUFFIXES)})
    counts = dict(introduced=0, retained=0, resolved=0)
    tested = failed = 0
    by_case = {}
    report.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(report, 'xt') as output:
        def record(key, values, index=None):
            nonlocal tested, failed
            tested += 1
            sample = index is not None
            steps = 0 if key.endswith('.master') else pcm_steps
            residual = RESIDUALS.get(key, 0) if residuals else 0
            wrong = [bad(values[0], v, sample, exact, steps, residual) for v in values[1:]]
            if not any(wrong): return
            failed += 1
            case = key.split('.')[0]
            by_case[case] = by_case.get(case, 0) + 1
            row = dict(key=key)
            if sample: row['sample'] = index
            if before is None: row.update(reference=safe(values[0]), candidate=safe(values[1]))
            else:
                status = 'retained' if all(wrong) else 'resolved' if wrong[0] else 'introduced'
                counts[status] += 1
                row.update(native=safe(values[0]), before=safe(values[1]), after=safe(values[2]), status=status)
            output.write(json.dumps(row, allow_nan=False)+'\n')
        for key in fields:
            values = [m.get(key, MISSING) for m in manifests]
            record(key, values)
            data = [samples(root, meta) for root, meta in zip(roots, values)]
            frames = limits.get(key)
            channels = values[0].get('channels', 2) if isinstance(values[0], dict) else 2
            count = max(map(len, data)) if frames is None else min(max(map(len, data)), channels*frames)
            for i in range(count):
                record(key, [v[i] if i < len(v) else MISSING for v in data], i)
        result = dict(tested=tested, failed=failed, exact=exact, atol=0 if exact else ATOL,
                      rtol=0 if exact else RTOL, pcm_steps=pcm_steps,
                      residual_atol=RESIDUALS if residuals else {},
                      frame_limits={k: v for k, v in keys.items() if v is not None},
                      failing_values_by_case=by_case,
                      roots=list(map(str, roots)), counts=counts)
        output.write(json.dumps(dict(summary=result))+'\n')
    report.with_suffix('').with_suffix('.summary.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result), flush=True)
    return failed if before is None else counts['introduced']


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
    p.add_argument('--profile', choices=('all',), default='all')
    p.add_argument('--reference', type=Path, help='Override only for independent Native agreement')
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--before', type=Path)
    p.add_argument('--report', type=Path, required=True, help='Fresh .jsonl.gz report, plus summary JSON')
    p.add_argument('--exact', action='store_true')
    p.add_argument('--pcm-steps', type=int, choices=(0, 1), default=0,
                   help='Admit one 1/32768 step of device PCM (never master floats)')
    p.add_argument('--residuals', action='store_true',
                   help='Admit the recorded RESIDUALS bounds of common.py (gate only)')
    a = p.parse_args()
    if not a.report.name.endswith('.jsonl.gz'): p.error('report must end in .jsonl.gz')
    reference = a.reference or FIXTURE/'Reference'/('unity-'+a.release)/('rate-'+str(a.rate))
    assert header(reference)['release'] == a.release and header(reference)['rate'] == a.rate
    raise SystemExit(bool(compare(reference, a.candidate, a.report, profile(a.profile, a.rate), a.exact, a.before, a.pcm_steps, a.residuals)))
