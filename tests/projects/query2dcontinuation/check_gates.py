#!/usr/bin/env python3
"""Compare fresh gates without invoking historical report writers."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[2]
WORK=Path(os.environ['QUERY_WORK'])
OUT=ROOT/'Evidence/gates'
OUT.mkdir(parents=True,exist_ok=True)
SCENE=ROOT.parent/'physicsscenesettings'
ACCEPTED=ROOT.parent/'trigger2dlifecycle/Evidence/ParentIntegration/accepted'
spec=importlib.util.spec_from_file_location('cmp',ROOT.parent/'query2dparity/compare.py')
mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
spec=importlib.util.spec_from_file_location('scene',SCENE/'compare.py')
scene=importlib.util.module_from_spec(spec);spec.loader.exec_module(scene)
def load(p):return json.loads(p.read_text())
def archive(p,r):
    dest=OUT/r;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
def dump(p,v):p.write_text(json.dumps(v,indent=2)+'\n')
summary={}
for name,native,p in [
    ('physics',ROOT.parent/'physics/Reference/unity-6000.0.71f1.json',Path(os.environ['SCENE_WORK'])/'regressions-final/physics.json'),
    ('timing',ROOT.parent/'trigger2dtiming/Evidence/accepted/timing-expanded-native1.json',Path(os.environ['TRIGGER_WORK'])/'timing-final.json'),
    ('order',ROOT.parent/'trigger2dtiming/Evidence/order/order-native1.json',Path(os.environ['TRIGGER_WORK'])/'order-final.json')]:
    r=mod.compare(load(native),load(p));before=mod.compare(load(native),load(ACCEPTED/'regressions'/(name+'.json')))
    bad=lambda r:{(v['scenario'],v['key']) for v in r['failures']}
    r.update(resolved=sorted(bad(before)-bad(r)),retained=sorted(bad(r)&bad(before)),introduced=sorted(bad(r)-bad(before)))
    if name=='physics':
        starting=mod.compare(load(native),load(WORK/'physics-starting.json'))
        r['vs_starting_candidate']={key:sorted(value) for key,value in dict(resolved=bad(starting)-bad(r),retained=bad(starting)&bad(r),introduced=bad(r)-bad(starting)).items()}
        dump(OUT/'physics-starting-comparison.json',starting)
        archive(WORK/'physics-starting.json',Path('captures')/'physics-starting.json')
    dump(OUT/(name+'-comparison.json'),r);archive(p,Path('captures')/p.name)
    assert not r['introduced'],name
    if name!='physics':assert not r['failures'],name
    summary[name]={k:len(v) if isinstance(v,list) else {kk:len(vv) for kk,vv in v.items()} if isinstance(v,dict) else v for k,v in r.items()}
for name in ['lifecycle','receivers']:
    ref=ROOT.parent/'trigger2dlifecycle/Evidence/native'/(name+'-native1.json')
    p=Path(os.environ['TRIGGER_WORK'])/(name+'-final.json')
    assert load(ref)==load(p),name
    summary['contact-'+name]=dict(records=sum(map(len,load(ref).values())),exact=True)
    archive(p,Path('captures')/('contact-'+p.name))
summary['loading']={}
for order in ['transform-first','rigidbody-first']:
    ref=ROOT.parent/'inactive3dloading/Evidence/final'/(order+'-native1.jsonl')
    p=WORK/'loading'/(order+'-final.jsonl')
    a,b=scene.read(str(ref)),scene.read(str(p));assert a==b and len(a)==4656,order
    summary['loading'][order]=dict(records=len(a),exact=True);archive(p,Path('loading')/p.name)
summary['focused']={}
for prior in (ACCEPTED/'focused').glob('*.jsonl'):
    p=Path(os.environ['SCENE_PARENT_WORK'])/(prior.stem+'-final.jsonl')
    a,b=scene.read(str(prior)),scene.read(str(p));assert a.keys()==b.keys() and all(scene.matches(k,a[k],b[k]) for k in a),prior.stem
    if prior.stem not in ['order','component','barrier']:assert a==b,prior.stem
    summary['focused'][prior.stem]=dict(records=len(a),exact=sum(a[k]==b[k] for k in a));archive(p,Path('focused')/prior.name)
summary['matrix']=load(Path(os.environ['TRIGGER_WORK'])/'matrix/summary.json')
assert summary['matrix']['matched']==5055 and summary['matrix']['exact']==4803
assert len(summary['matrix']['profiles'])==21 and all(p['byte_identical_to_accepted'] for p in summary['matrix']['profiles'])
for p in (Path(os.environ['TRIGGER_WORK'])/'matrix').glob('*'):
    if p.is_file():archive(p,Path('matrix')/p.name)
for name in ['physicsdefaults','joints','controller','forces','rayhits','joints2d','effectors','graphics','canvaslifecycle','messages']:
    fixture=ROOT.parent/name
    if name in ['joints2d','effectors','canvaslifecycle']:p=Path(os.environ['TRIGGER_WORK'])/name
    elif name in ['graphics','messages']:p=Path(os.environ['TRIGGER_WORK'])/(name+('.json' if name=='graphics' else '.jsonl'))
    elif name=='physicsdefaults':p=Path(os.environ['SCENE_WORK'])/'regressions-final/physicsdefaults.jsonl'
    else:p=Path(os.environ['SCENE_WORK'])/'regressions-final'/name
    ref=fixture/'Reference'
    if p.is_file():ref=ref/('unity-6000.0.71f1'+p.suffix)
    with (OUT/(name+'-comparison.log')).open('w') as log:
        subprocess.run(['python3',str(fixture/'compare.py'),str(ref),str(p)],stdout=log,stderr=subprocess.STDOUT,check=True)
    if p.is_file():archive(p,Path('regressions')/p.name)
    else:
        for raw in p.glob('*.jsonl'):archive(raw,Path('regressions')/name/raw.name)
    summary[name]='existing comparator passed; see complete comparison log'
p=Path(os.environ['TRIGGER_WORK'])/'lifecycle.jsonl'
assert p.read_bytes()==(ROOT.parent/'lifecycle/Reference/unity-6000.0.71f1.jsonl').read_bytes()
archive(p,Path('regressions')/p.name);summary['lifecycle']='byte-identical to Native'
trx=WORK/'validation/integrated.trx'
c=ET.parse(trx).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert c['total']==c['passed'] and c['failed']==c['notExecuted']=='0';summary['tests']=c
archive(trx,Path('validation')/trx.name)
summary['api']='Parent-owned; not run here'
dump(OUT/'summary.json',summary)
print({k:v for k,v in summary.items() if k not in ['matrix','focused']})
