#!/usr/bin/env python3
"""Stream every failing sample in the complete Native/candidate or before/after union."""
import argparse
import array
import gzip
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
    if not isinstance(meta,dict) or "file" not in meta: return b""
    file=root/meta["file"]
    if file.exists(): return file.read_bytes()
    packed=file.with_suffix(file.suffix+".gz")
    return gzip.open(packed,"rb").read() if packed.exists() else b""


def samples(root, meta):
    values = array.array('f')
    data=raw(root,meta)
    if len(data)%4: raise ValueError('Partial float in '+str(root)+'/'+str(meta))
    values.frombytes(data)
    if __import__('sys').byteorder != 'little': values.byteswap()
    return values



def bad(a, b, sample, exact):
    if not sample: return a != b
    if not isinstance(a, float) or not isinstance(b, float): return a != b
    if not math.isfinite(a) or not math.isfinite(b): return True
    return struct.pack('<f', a) != struct.pack('<f', b) if exact else abs(a-b) > ATOL+RTOL*abs(a)


def safe(value):
    return repr(value) if isinstance(value, float) and not math.isfinite(value) else value


def compare(reference, candidate, report, exact=False, before=None):
    roots = [reference, candidate] if before is None else [reference, before, candidate]
    manifests = [header(root) for root in roots]
    counts = dict(introduced=0, retained=0, resolved=0)
    tested = failed = bit_changed = tolerated_changed = 0
    report.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(report, 'xt') as output:
        def record(key, values, index=None):
            nonlocal tested, failed, bit_changed, tolerated_changed
            tested += 1
            sample = index is not None
            wrong = [bad(values[0], v, sample, exact) for v in values[1:]]
            if sample and not any(v == MISSING for v in values):
                changed = len({struct.pack('<f', v) for v in values}) > 1
                bit_changed += changed
                tolerated_changed += changed and not any(wrong)
            if not any(wrong): return
            failed += 1
            row = dict(key=key)
            if sample: row['sample'] = index
            if before is None: row.update(reference=safe(values[0]), candidate=safe(values[1]))
            else:
                status = 'retained' if all(wrong) else 'resolved' if wrong[0] else 'introduced'
                counts[status] += 1
                row.update(native=safe(values[0]), before=safe(values[1]), after=safe(values[2]), status=status)
            output.write(json.dumps(row, allow_nan=False)+'\n')
        for key in sorted(set().union(*(m.keys() for m in manifests))):
            values = [m.get(key, MISSING) for m in manifests]
            record(key, values)
            data = [raw(root, meta) for root, meta in zip(roots, values)]
            for start in range(0,max(map(len,data)),4096):
                chunks=[v[start:start+4096] for v in data]
                if len({v for v in chunks})==1:
                    tested+=len(chunks[0])//4
                    continue
                floats=[]
                for chunk in chunks:
                    if len(chunk)%4: raise ValueError('Partial float for '+key)
                    floats.append(struct.unpack('<'+'f'*(len(chunk)//4),chunk))
                for i in range(max(map(len,floats))):
                    record(key,[v[i] if i<len(v) else MISSING for v in floats],start//4+i)
        for root,manifest in zip(roots,manifests):
            expected={v['file'] for v in manifest.values() if isinstance(v,dict) and 'file' in v}
            actual={p.name.removesuffix('.gz') for p in root.iterdir() if p.name!='capture.json'}
            for file in sorted(actual-expected):
                record('extra_file:'+str(root/file),[MISSING,file])
        result = dict(tested=tested, failed=failed, exact=exact, atol=0 if exact else ATOL,
                      rtol=0 if exact else RTOL, bit_changed=bit_changed, tolerated_changed=tolerated_changed,
                      roots=list(map(str, roots)), counts=counts)
        output.write(json.dumps(dict(summary=result))+'\n')
    report.with_suffix('').with_suffix('.summary.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result), flush=True)
    return failed if before is None else counts['introduced']


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
    p.add_argument('--reference', type=Path, help='Override only for independent Native agreement')
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--before', type=Path)
    p.add_argument('--report', type=Path, required=True, help='Fresh .jsonl.gz report, plus summary JSON')
    p.add_argument('--exact', action='store_true')
    a = p.parse_args()
    if not a.report.name.endswith('.jsonl.gz'): p.error('report must end in .jsonl.gz')
    reference = a.reference or Path(__file__).parent/'Reference'/('unity-'+a.release)/('rate-'+str(a.rate))
    assert header(reference)['release'] == a.release and header(reference)['rate'] == a.rate
    raise SystemExit(bool(compare(reference, a.candidate, a.report, a.exact, a.before)))
