#!/usr/bin/env python3
"""Union-key comparisons retain every failure and passing nonexact observation."""
import argparse, collections, gzip, hashlib, importlib.util, json, os, shutil
from pathlib import Path
ROOT = Path(__file__).resolve().parent
WORK = Path(os.environ['MOTION_WORK'])
os.environ['TRIGGER_WORK'] = str(WORK)
spec = importlib.util.spec_from_file_location('comparison', ROOT.parent/'trigger2dtiming/report.py')
comparison = importlib.util.module_from_spec(spec); spec.loader.exec_module(comparison)
p=argparse.ArgumentParser();p.add_argument('label');p.add_argument('--native',default='native1');p.add_argument('--baseline',default='baseline');p.add_argument('--archive');a=p.parse_args()
dest=ROOT/'Evidence'/(a.archive or a.label)
if dest.exists(): raise SystemExit('Refusing to replace '+str(dest))
paths=[WORK/(s+'.json') for s in [a.native,a.baseline,a.label]]
ref,old,new=[json.loads(f.read_text()) for f in paths]
dest.mkdir()
result={}
for group,scenarios in [('motion',list(ref)),('broad',[k for k in ref if k.startswith(('2d.','3d.'))])]:
    r,b,c=[{k:d[k] for k in scenarios if k in d} for d in [ref,old,new]]
    before,after=[comparison.compare(r,d,1e-3) for d in [b,c]]
    bad=lambda x:{(v['scenario'],v['key']) for v in x['failures']}
    after.update(resolved=sorted(bad(before)-bad(after)),introduced=sorted(bad(after)-bad(before)),retained=sorted(bad(before)&bad(after)))
    result[group]={'before':before,'after':after}
    print(group,after['matched'],'/',after['records'],'exact',after['exact'],'resolved',len(after['resolved']),'introduced',len(after['introduced']),'retained',len(after['retained']),flush=True)
    print('failure scenarios',dict(collections.Counter(x['scenario'] for x in after['failures'])),flush=True)
with gzip.open(dest/'comparison.json.gz','wt') as f: json.dump(result,f)
manifest={}
for src in paths:
    manifest[str(src)]=hashlib.sha256(src.read_bytes()).hexdigest()
    with src.open('rb') as i,gzip.open(dest/(src.name+'.gz'),'wb') as o: shutil.copyfileobj(i,o)
for src in WORK.glob(a.label+'*.log'): shutil.copy2(src,dest/src.name)
(dest/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
