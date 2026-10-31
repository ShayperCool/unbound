#!/usr/bin/env python3
"""Retain every failing Native/before/after value, including new and missing samples."""
import argparse
import array
import json
import math
from pathlib import Path
from compare import ATOL, RTOL

MISSING = '<missing>'

def capture(root):
    path = root/'capture.json'
    header = json.loads(path.read_text()) if path.exists() else {}
    data = {}
    for key, value in header.items():
        if isinstance(value, dict) and 'file' in value:
            values = array.array('f')
            file = root/value['file']
            if file.exists(): values.frombytes(file.read_bytes())
            data[key] = values
    return header, data


def bad(a, b, sample):
    if sample and isinstance(a, (int,float)) and isinstance(b, (int,float)):
        return not math.isfinite(a) or not math.isfinite(b) or abs(a-b) > ATOL+RTOL*abs(a)
    return a != b


def safe(value):
    return repr(value) if isinstance(value,float) and not math.isfinite(value) else value


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--release',required=True)
    p.add_argument('--rate',type=int,choices=(44100,48000),default=48000)
    p.add_argument('--profile',choices=('matrix','startup','renderer-api','renderer-pcm','gain'),default='matrix')
    p.add_argument('--before',type=Path,required=True)
    p.add_argument('--after',type=Path,required=True)
    p.add_argument('--report',type=Path,required=True)
    a=p.parse_args()
    if a.profile != 'matrix' and a.rate != 48000:
        p.error('this additional profile is measured at 48000 Hz only')
    native=Path(__file__).parent/'Reference'/('unity-'+a.release)/('rate-'+str(a.rate) if a.profile == 'matrix' else a.profile)
    captures=[capture(root) for root in (native,a.before,a.after)]
    keys=set().union(*(header.keys() for header,data in captures))
    counts=dict(introduced=0,retained=0,resolved=0)
    rows=[];tested=0
    def record(key,values,index=None):
        nonlocal tested
        tested+=1
        b,c=bad(values[0],values[1],index is not None),bad(values[0],values[2],index is not None)
        if not b and not c:return
        status='retained' if b and c else 'resolved' if b else 'introduced'
        counts[status]+=1
        row=dict(key=key,native=safe(values[0]),before=safe(values[1]),after=safe(values[2]),status=status)
        if index is not None: row['sample']=index
        rows.append(row)
    for key in sorted(keys):
        values=[header.get(key,MISSING) for header,data in captures]
        record(key,values)
        samples=[data.get(key,()) for header,data in captures]
        for i in range(max(map(len,samples))):
            record(key,[s[i] if i<len(s) else MISSING for s in samples],i)
    result=dict(release=a.release,rate=a.rate,profile=a.profile,tested=tested,atol=ATOL,rtol=RTOL,counts=counts,failures=rows)
    a.report.write_text(json.dumps(result,indent=2,allow_nan=False)+'\n')
    print(json.dumps(dict(tested=tested,**counts)))

if __name__=='__main__':main()
