#!/usr/bin/env python3
"""Union-key comparison. Raw IDs remain captured and checked through named identities."""
import argparse,json,math
from pathlib import Path
def read(path):
    values={}
    for row in map(json.loads,Path(path).read_text().splitlines()):
        if row['key'] in values:raise ValueError('duplicate key '+row['key'])
        values[row['key']]=row['value']
    names={0:'null'}
    for k,v in values.items():
        if '/identity/' in k:
            name=k.replace('/identity/','/')
            if v in names and names[v]!=name:raise ValueError('reused object ID '+str(v))
            names[v]=name
    normalized={}
    for k,v in values.items():
        if '/identity/' in k or k.endswith(('bodyRaw','otherBodyRaw','colliderRaw','otherColliderRaw')):
            if v not in names:raise ValueError('unresolved raw ID '+str((k,v)))
            normalized[k]=names[v]
        else:normalized[k]=v
    return normalized
def compare(a,b,numeric=False):
    diff=[];exact=0;matched=0
    for k in sorted(a.keys()|b.keys()):
        if k not in a or k not in b:diff.append(dict(key=k,reference=a.get(k),candidate=b.get(k),reason='missing/extra'));continue
        x,y=a[k],b[k]
        if x==y:exact+=1;matched+=1;continue
        # Fixed float bound only for physical float data, never IDs/counts/order/layout/flags.
        physical=any('/'+s+'/' in k for s in ['position','normal','impulse','sum']) or k.endswith('/separation')
        if numeric and physical and type(x) in (int,float) and type(y) in (int,float) and math.isfinite(x) and math.isfinite(y) and abs(x-y)<=1e-5:
            matched+=1;continue
        if numeric and k.endswith('/point') and isinstance(x,list) and isinstance(y,list) and len(x)==len(y) and all(math.isfinite(u) and math.isfinite(v) and abs(u-v)<=1e-5 for u,v in zip(x,y)):
            matched+=1;continue
        diff.append(dict(key=k,reference=x,candidate=y))
    return dict(records=len(a.keys()|b.keys()),exact=exact,matched=matched,differences=diff)
def main():
    p=argparse.ArgumentParser();p.add_argument('reference');p.add_argument('candidate');p.add_argument('--numeric',action='store_true');p.add_argument('--output',required=True);a=p.parse_args();out=Path(a.output)
    if out.exists():raise SystemExit('refusing overwrite '+str(out))
    r=compare(read(a.reference),read(a.candidate),a.numeric);out.write_text(json.dumps(r,indent=2)+'\n');print({k:v if k!='differences' else len(v) for k,v in r.items()})
if __name__=='__main__':main()
