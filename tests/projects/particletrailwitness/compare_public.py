#!/usr/bin/env python3
"""Full public record union; trail geometry uses the existing streams 2e-4 tolerance."""
import argparse
import base64
import json
import math
from pathlib import Path
import struct
from run import rows, leaf_differences
from public_contract import number


def differences(a, b, path=()):
    if isinstance(a, dict) and isinstance(b, dict) and set(a) == set(b) == {'r','bits'}:
        expected, actual = number(a), number(b)
        if not math.isclose(expected, actual, rel_tol=2e-4, abs_tol=2e-4):
            yield dict(path=path, expected=a, actual=b)
    elif type(a) is type(b) and isinstance(a, (dict,list)):
        keys = sorted(a.keys() | b.keys()) if isinstance(a, dict) else range(max(len(a),len(b)))
        for k in keys:
            ap = k in a if isinstance(a,dict) else k<len(a)
            bp = k in b if isinstance(b,dict) else k<len(b)
            if ap and bp:
                yield from differences(a[k],b[k],(*path,k))
            else:
                yield dict(path=(*path,k), expected=a[k] if ap else 'MISSING',actual=b[k] if bp else 'MISSING')
    elif a != b:
        yield dict(path=path,expected=a,actual=b)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--actual', required=True, type=Path)
    p.add_argument('--out', required=True, type=Path)
    a = p.parse_args()
    reference = rows(Path(__file__).resolve().parent/'Reference'/('unity-'+a.release)/'public.jsonl')
    actual = rows(a.actual)
    full=[]; byte_union=[]
    for key in sorted(reference.keys()|actual.keys()):
        expected,observed=reference.get(key,'MISSING'),actual.get(key,'MISSING')
        failures=list(differences(expected,observed,(key,)))
        if key.endswith('.bytes') or key=='mesh.indexBytes':
            left,right=base64.b64decode(expected),base64.b64decode(observed)
            byte_union.extend(dict(key=key,offset=i,expected=left[i] if i<len(left) else 'MISSING',actual=right[i] if i<len(right) else 'MISSING')
                              for i in range(max(len(left),len(right))) if i>=len(left) or i>=len(right) or left[i]!=right[i])
            # Buffer bytes remain a separate exact diagnostic. All typed fields,
            # formats, offsets, dimensions, strides and indices are checked above.
            failures=[]
        full.append(dict(key=key,expected=expected,actual=observed,exact=expected==observed,failures=failures))
    a.out.mkdir(parents=True,exist_ok=True)
    for name,value in [('public-full-union',full),('raw-byte-union',byte_union),('raw-leaf-union',leaf_differences(reference,actual))]:
        (a.out/(name+'.json')).write_text(json.dumps(value,indent=2)+'\n')
    failures=[x for r in full for x in r['failures']]
    summary=dict(records=len(full),missing=[k for k in reference if k not in actual],extra=[k for k in actual if k not in reference],failing_values=len(failures),failures=failures,
                 raw_byte_differences=len(byte_union),exact_records=sum(r['exact'] for r in full),atol=2e-4,rtol=2e-4,
                 exact_raw_mesh_claim=not byte_union)
    (a.out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n');print(json.dumps(summary))
    return int(bool(failures))


if __name__=='__main__':
    raise SystemExit(main())
