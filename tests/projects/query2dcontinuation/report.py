#!/usr/bin/env python3
"""Preserve every union-key observation and both baseline deltas without exclusions."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
WORK = Path(os.environ['QUERY_WORK'])
OLD = ROOT.parent / 'query2dparity/Evidence'
OUT = ROOT / 'Evidence'
spec = importlib.util.spec_from_file_location('query_compare', ROOT.parent / 'query2dparity/compare.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
compare = module.compare

def load(p): return json.loads(p.read_text())
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def bad(r): return {(v['scenario'], v['key']) for v in r['failures']}
def dump(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(value, indent=2) + '\n')
def delta(a, b):
    return dict(resolved=sorted(bad(a)-bad(b)), retained=sorted(bad(a)&bad(b)), introduced=sorted(bad(b)-bad(a)))
def archive(p, relative):
    dest=OUT/relative
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(p, dest)

suites={
 'queries': (OLD/'native1.json', OLD/'native2.json'),
 'geometry': (OLD/'geometry-native1.json', OLD/'geometry-native2.json'),
 'selection': (OLD/'selection-native1.json', OLD/'selection-native2.json'),
 'selection-fine': (OLD/'fine-native1.json', OLD/'fine-native2.json'),
 'boundary': (OLD/'boundary-native1.json', OLD/'boundary-native2.json'),
 'tests': (OLD/'tests-native1.json', OLD/'tests-native2.json'),
 'skin': (ROOT.parent/'colliderskin2d/Evidence/native1.json', ROOT.parent/'colliderskin2d/Evidence/native2.json'),
 'initial': (WORK/'initial-native1.json', WORK/'initial-native2.json'),
}
summary=dict(starting_commit='29c7291dde2a85872617ff4bdfb551fe8a67eca5', canonical_accepted_commit='88a53c477dc43e646c21522f6c402c0cec98c6c1',strict_acceptance=False,unity='6000.0.71f1 Linux',suites={},private_work=str(WORK))
for name, (native, independent) in suites.items():
    assert native.read_bytes()==independent.read_bytes(),name
    ref=load(native)
    reports={}
    for stage in ['accepted','starting','final']:
        capture=WORK/('skin/'+stage+'.json' if name=='skin' else name+'-'+stage+'.json')
        r=compare(ref,load(capture))
        archive(capture,Path('captures')/(name+'-'+stage+'.json'))
        reports[stage]=r
    row=dict(native_sha256=sha(native),native_independent_sha256=sha(independent),native_records=sum(map(len,ref.values())),stages={})
    for stage,r in reports.items():
        if stage!='accepted':
            r['vs_canonical_accepted']=delta(reports['accepted'],r)
        if stage=='final':
            r['vs_starting_candidate']=delta(reports['starting'],r)
        dump(OUT/'comparisons'/(name+'-'+stage+'.json'),r)
        row['stages'][stage]={k:len(v) if isinstance(v,list) else {kk:len(vv) for kk,vv in v.items()} if isinstance(v,dict) else v for k,v in r.items()}
    summary['suites'][name]=row
# Every attempt, including rejected or interrupted runs, stays in the private tree.
for p in WORK.glob('*.json'):
    archive(p,Path('attempts')/p.name)
for p in WORK.glob('*.log'):
    archive(p,Path('logs')/p.name)
for p in (WORK/'validation').glob('*'):
    if p.is_file():archive(p,Path('validation')/p.name)
for p in (WORK/'rejected').rglob('*'):
    if p.is_file():archive(p,Path('rejected')/p.relative_to(WORK/'rejected'))
archive(WORK/'env.sh','validation/private-env.sh')
summary['artifacts_sha256']={str(p.relative_to(WORK)):sha(p) for base in ['managed-accepted','managed-start','managed-threshold1','managed-contact2','managed-final'] for p in sorted((WORK/base).rglob('*')) if p.is_file()}
summary['artifacts_sha256']['unbound']=sha(WORK/'unbound')
summary['source_sha256']={str(p.relative_to(REPO)):sha(p) for p in [REPO/'managed/UnityEngine/Physics2D/Physics2DQueries.cs',*sorted((REPO/'managed/UnityEngine/Physics2D').glob('QueryParity*.cs')),REPO/'managed/UnityEngine.Tests/Physics2DQueryParityTests.cs']}
summary['evidence_sha256']={str(p.relative_to(OUT)):sha(p) for p in sorted(OUT.rglob('*')) if p.is_file() and p!=OUT/'summary.json'}
dump(OUT/'summary.json',summary)
for name,row in summary['suites'].items():print(name,row['stages']['final'])
