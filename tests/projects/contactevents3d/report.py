#!/usr/bin/env python3
"""Preserve exact and fixed-bound union comparisons and all changes from a prior capture."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root
from compare import read, compare
from artifact_paths import artifact_path


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--unity-version',required=True)
    p.add_argument('--work',type=Path,required=True)
    p.add_argument('--label',required=True)
    p.add_argument('--baseline-label')
    p.add_argument('--suite-label',action='append',default=[],help='Override one suite label, e.g. capsule=certified')
    p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    artifact_path(a.work)
    artifact_path(a.output)
    import os
    os.environ['UNBOUND_PROBE_UNITY_VERSION']=a.unity_version
    if a.output.exists():p.error('refusing overwrite '+str(a.output))
    repo=Path(__file__).resolve().parents[3]
    if a.output.resolve().is_relative_to(repo):p.error('output must be outside repository')
    a.output.mkdir(parents=True)
    labels=dict(item.split('=',1) for item in a.suite_label)
    summary={}
    for suite,profile in [('contacts',i) for i in range(4)]+[(s,0) for s in ['controls','geometry','capsule','sweeps']]:
        name=suite+(f'-p{profile}' if suite=='contacts' else '')
        ref=reference_root(Path(__file__).resolve().parent)/(name+'.jsonl')
        label=labels.get(suite,a.label)
        candidate=a.work/f'{suite}-{label}-p{profile}.jsonl'
        native,actual=read(ref),read(candidate)
        summary[name]={}
        for mode in ['exact','numeric']:
            r=compare(native,actual,mode=='numeric')
            if a.baseline_label:
                prior=read(a.work/f'{suite}-{a.baseline_label}-p{profile}.jsonl')
                before=compare(native,prior,mode=='numeric')
                old={v['key'] for v in before['differences']};new={v['key'] for v in r['differences']}
                r.update(resolved=sorted(old-new),retained=sorted(old&new),introduced=sorted(new-old))
            (a.output/(name+'-'+mode+'.json')).write_text(json.dumps(r,indent=2)+'\n')
            summary[name][mode]={k:len(v) if isinstance(v,list) else v for k,v in r.items()}
        summary[name]['sha256']={str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in [ref,candidate]}
    (a.output/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    print(json.dumps(summary,indent=2))


if __name__=='__main__':main()
