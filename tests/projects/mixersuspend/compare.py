#!/usr/bin/env python3
"""Keep every failing metadata value and sample in the complete comparison union."""
import argparse
import array
import gzip
import hashlib
import json
import math
from pathlib import Path
import struct

ATOL = 2e-5
RTOL = 2e-4
MISSING = '<missing>'


def header(root):
    file = root/'capture.json'
    return json.loads(file.read_text()) if file.exists() else {}


def raw(root, meta):
    if not isinstance(meta, dict) or 'file' not in meta:
        return b''
    file = root/meta['file']
    if file.exists():
        return file.read_bytes()
    packed = file.with_suffix(file.suffix+'.gz')
    return gzip.open(packed, 'rb').read() if packed.exists() else b''


def samples(root, meta):
    values = array.array('f')
    data = raw(root, meta)
    if len(data) % 4:
        raise ValueError('Partial float in '+str(root)+'/'+str(meta))
    values.frombytes(data)
    if __import__('sys').byteorder != 'little':
        values.byteswap()
    return values


def bad(a, b, sample, exact):
    if not sample or not isinstance(a, float) or not isinstance(b, float):
        return a != b
    if not math.isfinite(a) or not math.isfinite(b):
        return True
    return struct.pack('<f', a) != struct.pack('<f', b) if exact else abs(a-b) > ATOL+RTOL*abs(a)


def safe(value):
    return repr(value) if isinstance(value, float) and not math.isfinite(value) else value


def compare(reference, candidate, report, exact=False, before=None, batch=False):
    roots = [reference, candidate] if before is None else [reference, before, candidate]
    manifests = [header(root) for root in roots]
    # A failed capture can leave a partial file without its final metadata entry. Compare
    # those actual floats too, while retaining the missing metadata as a separate failure.
    declared = {v['file'] for m in manifests for v in m.values()
                if isinstance(v, dict) and 'file' in v}
    actual = [{p.name.removesuffix('.gz') for p in root.iterdir()
               if p.name.endswith(('.f32', '.f32.gz'))} for root in roots]
    for name in sorted(set().union(*actual)-declared):
        for files, meta in zip(actual, manifests):
            if name in files:
                meta['undeclared_file:'+name] = {'file': name, 'undeclared': True}
    counts = dict(introduced=0, retained=0, resolved=0)
    tested = failed = bit_changed = tolerated_changed = 0
    report.parent.mkdir(parents=True, exist_ok=True)
    np = None
    if batch:
        try:
            import numpy as np
        except ImportError:
            pass  # Portable standard-library path retains the same records.
    with gzip.open(report, 'xt', compresslevel=1 if batch else 6) as output:
        pending = []
        def write(row):
            output.write(json.dumps(row, allow_nan=False, separators=(',', ':'))+'\n')
        def flush():
            if not pending:
                return
            row = {'key': pending[0]['key'], 'samples': [r['sample'] for r in pending]}
            for field in pending[0]:
                if field not in ('key', 'sample'):
                    row[field] = [r[field] for r in pending]
            write(row)
            pending.clear()
        def record(key, values, index=None):
            nonlocal tested, failed, bit_changed, tolerated_changed
            tested += 1
            sample = index is not None
            wrong = [bad(values[0], v, sample, exact) for v in values[1:]]
            if sample and not any(v == MISSING for v in values):
                changed = len({struct.pack('<f', v) for v in values}) > 1
                bit_changed += changed
                tolerated_changed += changed and not any(wrong)
            if not any(wrong):
                return
            failed += 1
            row = dict(key=key)
            if sample:
                row['sample'] = index
            if before is None:
                row.update(reference=safe(values[0]), candidate=safe(values[1]))
            else:
                status = 'retained' if all(wrong) else 'resolved' if wrong[0] else 'introduced'
                counts[status] += 1
                row.update(native=safe(values[0]), before=safe(values[1]), after=safe(values[2]), status=status)
            if batch and sample:
                pending.append(row)
                if len(pending) == 8192:
                    flush()
            else:
                flush()
                write(row)
        for key in sorted(set().union(*(m.keys() for m in manifests))):
            values = [m.get(key, MISSING) for m in manifests]
            record(key, values)
            file = next((v['file'] for v in values if isinstance(v, dict) and 'file' in v), None)
            data = [raw(root, meta if isinstance(meta, dict) else {'file': file})
                    if file else b'' for root, meta in zip(roots, values)]
            for start in range(0, max(map(len, data)), 32768):
                chunks = [v[start:start+32768] for v in data]
                if any(len(c) % 4 for c in chunks):
                    raise ValueError('Partial float for '+key)
                if len(set(chunks)) == 1:
                    tested += len(chunks[0])//4
                    continue
                if np is not None and len({len(c) for c in chunks}) == 1:
                    floats = [np.frombuffer(c, dtype='<f4') for c in chunks]
                    x = floats[0].astype('f8')
                    wrong = [(floats[0].view('<u4') != v.view('<u4')) if exact
                             else np.abs(x-v.astype('f8')) > ATOL+RTOL*np.abs(x)
                             for v in floats[1:]]
                    for i, v in enumerate(floats[1:]):
                        wrong[i] |= ~np.isfinite(x) | ~np.isfinite(v)
                    changed = np.logical_or.reduce([floats[0].view('<u4') != v.view('<u4') for v in floats[1:]])
                    failing = np.logical_or.reduce(wrong)
                    tested += len(x)
                    bit_changed += int(np.count_nonzero(changed))
                    tolerated_changed += int(np.count_nonzero(changed & ~failing))
                    failed += int(np.count_nonzero(failing))
                    indexes = np.flatnonzero(failing)
                    if len(indexes):
                        flush()
                        row = dict(key=key, samples=(indexes+start//4).tolist())
                        fields = ['reference', 'candidate'] if before is None else ['native', 'before', 'after']
                        for field, v in zip(fields, floats):
                            row[field] = [safe(float(n)) for n in v[indexes]]
                        if before is not None:
                            statuses = np.where(wrong[0][indexes], np.where(wrong[1][indexes], 'retained', 'resolved'), 'introduced')
                            row['status'] = statuses.tolist()
                            for status in counts:
                                counts[status] += int(np.count_nonzero(statuses == status))
                        write(row)
                    continue
                floats = [struct.unpack('<'+'f'*(len(c)//4), c) for c in chunks]
                for i in range(max(map(len, floats))):
                    record(key, [v[i] if i < len(v) else MISSING for v in floats], start//4+i)
            flush()
        for number, root in enumerate(roots):
            for file in sorted(root.iterdir()):
                if file.name == 'capture.json' or file.name.endswith(('.f32', '.f32.gz')):
                    continue
                values = [MISSING]*len(roots)
                values[number] = dict(file=file.name, bytes=file.stat().st_size,
                                      sha256=hashlib.sha256(file.read_bytes()).hexdigest())
                record('extra_file:'+file.name, values)
        result = dict(tested=tested, failed=failed, exact=exact, atol=0 if exact else ATOL,
                      rtol=0 if exact else RTOL, bit_changed=bit_changed, tolerated_changed=tolerated_changed,
                      roots=list(map(str, roots)), counts=counts, sample_arrays=batch)
        write(dict(summary=result))
    report.with_suffix('').with_suffix('.summary.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result), flush=True)
    return failed if before is None else counts['introduced']


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
    p.add_argument('--reference', type=Path)
    p.add_argument('--profile', choices=('suspension','submission','lifecycle'), default='suspension')
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--before', type=Path)
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--exact', action='store_true')
    p.add_argument('--batch', action='store_true', help='Explicit parallel sample/value arrays; optional NumPy acceleration')
    a = p.parse_args()
    if not a.report.name.endswith('.jsonl.gz'):
        p.error('report must end in .jsonl.gz')
    prefix = '' if a.profile == 'suspension' else a.profile+'-'
    reference = a.reference or Path(__file__).parent/'Reference'/('unity-'+a.release)/(prefix+'rate-'+str(a.rate))
    assert header(reference)['release'] == a.release and header(reference)['rate'] == a.rate
    raise SystemExit(bool(compare(reference, a.candidate, a.report, a.exact, a.before, a.batch)))
