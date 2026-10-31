#!/usr/bin/env python3
"""Archive fresh final gates and all failures, without changing historical reports."""
import argparse, collections, gzip, hashlib, importlib.util, json, os, shutil, subprocess, sys
import xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parent; REPO=ROOT.parents[2]; WORK=Path(os.environ['MOTION_WORK'])
parser=argparse.ArgumentParser();parser.add_argument('--archive',default='validation');parser.add_argument('--motion-report',default='final');args=parser.parse_args()
DEST=ROOT/'Evidence'/args.archive; DEST.mkdir(exist_ok=False)
SCENE=ROOT.parent/'physicsscenesettings'
sys.path.insert(0,str(SCENE));from compare import read,matches
os.environ['TRIGGER_WORK']=str(WORK)
spec=importlib.util.spec_from_file_location('motion_compare',ROOT.parent/'trigger2dtiming/report.py');cmp=importlib.util.module_from_spec(spec);spec.loader.exec_module(cmp)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def archive(p,name):
 d=DEST/name;d.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,d)
def load(p):return json.loads(p.read_text())
summary=dict(base='88a53c477dc43e646c21522f6c402c0cec98c6c1',source_commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),main_abi=5,physx_shim=8,shader_cache=13,focused=[],comparators={})
for name,count in [('lifecycle',25356),('receivers',1152),('timing',18491),('order',5720)]:
 if name in ['lifecycle','receivers']:ref=ROOT.parent/'trigger2dlifecycle/Evidence/native'/(name+'-native1.json')
 elif name=='timing':ref=ROOT.parent/'trigger2dtiming/Evidence/accepted/timing-expanded-native1.json'
 else:ref=ROOT.parent/'trigger2dtiming/Evidence/order/order-native1.json'
 assert ref.read_bytes()==ref.with_name(ref.name.replace('native1','native2')).read_bytes()
 candidate=WORK/(name+'-final.json');r=cmp.compare(load(ref),load(candidate),1e-3)
 assert r['matched']==r['records']==count and not r['failures'],name
 if name in ['lifecycle','receivers']:assert r['exact']==count
 if name=='timing':assert r['exact']==18445
 if name=='order':assert r['exact']==5716
 assert not any(v['key'].endswith(('/step','/kind','/phase','/self','/other','/sleep','/touching')) for v in r['rounding'])
 summary[name]=r;archive(candidate,Path('contact-lifecycle')/candidate.name)
for name in ['clone-placement','transform-clone-placement','sleep-inactive','inactive-sequences','sleep','triggers','flags','trigger-pairs','ignore-lifecycle','kinematic','order','component','barrier']:
 if name in ['clone-placement','transform-clone-placement']:ref=SCENE/'ClonePlacementEvidence'/(name+'-native1.jsonl')
 elif name=='inactive-sequences':ref=SCENE/'InactiveEvidence/inactive-sequences-native1.jsonl'
 elif name in ['sleep-inactive','sleep']:ref=SCENE/'SleepEvidence'/(name+'-native1.jsonl')
 elif name in ['flags','trigger-pairs','ignore-lifecycle']:ref=SCENE/'SDKLifecycleEvidence/Reference'/(name+'.jsonl')
 else:ref=SCENE/'ParentEvidence/Reference'/(name+'.jsonl')
 candidate=Path(os.environ['SCENE_PARENT_WORK'])/(name+'-final.jsonl');a,b=map(lambda p:read(str(p)),[ref,candidate])
 assert a.keys()==b.keys() and all(matches(k,a[k],b[k]) for k in a),name
 if name not in ['order','component','barrier']:assert a==b,name
 summary['focused'].append(dict(probe=name,records=len(a),matched=len(a),exact=sum(a[k]==b[k] for k in a)))
 archive(candidate,Path('focused')/candidate.name)
summary['loading']=[]
for order in ['transform-first','rigidbody-first']:
 ref=ROOT.parent/'inactive3dloading/Evidence/final'/(order+'-native1.jsonl');assert ref.read_bytes()==ref.with_name(order+'-native2.jsonl').read_bytes()
 candidate=WORK/'loading'/(order+'-final.jsonl');a,b=map(lambda p:read(str(p)),[ref,candidate]);assert a==b and len(a)==4656
 summary['loading'].append(dict(order=order,records=4656,matched=4656,exact=4656));archive(candidate,Path('loading')/candidate.name)
summary['matrix']=load(WORK/'matrix/summary.json');assert summary['matrix']['matched']==summary['matrix']['records']==5055 and summary['matrix']['exact']==4803
assert len(summary['matrix']['profiles'])==21 and all(v['byte_identical_to_accepted'] and not v['failed_keys'] for v in summary['matrix']['profiles'])
for p in (WORK/'matrix').iterdir():
 if p.is_file():archive(p,Path('matrix')/p.name)
ref=load(ROOT.parent/'physics/Reference/unity-6000.0.71f1.json');old=load(ROOT.parent/'trigger2dlifecycle/Evidence/ParentIntegration/accepted/regressions/physics.json');new=load(WORK/'physics-final.json')
before,after=[cmp.compare(ref,d,1e-3) for d in [old,new]]
bad=lambda r:{(v['scenario'],v['key']) for v in r['failures']}
after.update(resolved=sorted(bad(before)-bad(after)),introduced=sorted(bad(after)-bad(before)),retained=sorted(bad(before)&bad(after)))
assert not after['introduced'] and after['matched']==11505 and len(after['failures'])==121
assert all(old[k]==new[k] for k in old if k.startswith('3d.'))
summary['physics']=after;archive(WORK/'physics-final.json',Path('regressions/physics.json'))
motion_report=ROOT/'Evidence'/args.motion_report
with gzip.open(motion_report/'final.json.gz','rb') as f:assert f.read()==(WORK/'final.json').read_bytes(), 'motion report must describe this actual replay'
with gzip.open(motion_report/'comparison.json.gz','rt') as f:r=json.load(f)
assert not r['motion']['after']['introduced'];summary['new_motion']={k:v for k,v in r['motion']['after'].items() if k not in ['rounding','failures','retained']}
summary['new_motion']['retained_failures']=len(r['motion']['after']['failures']);summary['new_motion']['complete_report']='../'+args.motion_report+'/comparison.json.gz'
for name in ['physicsdefaults','joints','controller','rayhits','joints2d','effectors','forces','graphics','canvaslifecycle','messages']:
 fixture=ROOT.parent/name
 if name in ['joints2d','effectors','forces']:candidate=WORK/(name+'-final');ref=fixture/'Reference'
 elif name=='physicsdefaults':candidate=Path(os.environ['SCENE_WORK'])/'regressions-final/physicsdefaults.jsonl';ref=fixture/'Reference/unity-6000.0.71f1.jsonl'
 elif name in ['graphics','messages']:candidate=WORK/(name+('.json' if name=='graphics' else '.jsonl'));ref=fixture/'Reference'/('unity-6000.0.71f1'+candidate.suffix)
 elif name=='canvaslifecycle':candidate=WORK/name;ref=fixture/'Reference'
 else:candidate=Path(os.environ['SCENE_WORK'])/'regressions-final'/name;ref=fixture/'Reference'
 log=DEST/(name+'-comparison.log')
 with log.open('w') as f:subprocess.run(['python3',str(fixture/'compare.py'),str(ref),str(candidate)],stdout=f,stderr=subprocess.STDOUT,check=True)
 summary['comparators'][name]=log.read_text()
 if candidate.is_file():archive(candidate,Path('regressions')/candidate.name)
 else:
  for p in candidate.glob('*.jsonl'):archive(p,Path('regressions')/name/p.name)
assert '8671/8671' in summary['comparators']['canvaslifecycle'] and '316/316' in summary['comparators']['messages']
assert len(load(WORK/'graphics.json')['cases'])==80
assert (WORK/'lifecycle.jsonl').read_bytes()==(ROOT.parent/'lifecycle/Reference/unity-6000.0.71f1.jsonl').read_bytes();archive(WORK/'lifecycle.jsonl',Path('regressions/lifecycle.jsonl'))
summary['visuals']=dict(graphics=80,canvas=8671,messages=316,lifecycle_native_byte_identical=True)
trx=WORK/'tests/final.trx';summary['tests']=ET.parse(trx).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert summary['tests']['total']==summary['tests']['passed']=='430' and summary['tests']['failed']=='0';archive(trx,Path('tests/final.trx'))
for name in ['build-final.log','tests-final.log']:archive(WORK/name,Path('logs')/name)
for name in ['baseline','candidate1','final']:
 for suffix in ['.json','.log','-comparison.json']:
  p=WORK/('skin-'+name+suffix)
  if p.exists():archive(p,Path('skin')/p.name)
for name in ['skin1','skin2','candidate4']:
 for p in [WORK/('tests-'+name+'.log'),WORK/'tests'/(name+'.trx')]:
  if p.exists():archive(p,Path('candidate-tests')/p.name)
summary['skin_query_dependency']=dict(records=644,legacy_query_matched=473,legacy_query_retained_failures=171,introduced=0,fixture_transition_tests=46,query_candidate_not_merged=True)
a,b=[cmp.compare(load(ROOT/'Evidence/skin-dependency/native1.json'),load(WORK/('skin-'+name+'.json')),1e-3) for name in ['baseline','final']]
assert bad(a)==bad(b) and len(a['failures'])==171
summary['skin_query_dependency']['comparison']=b
summary['snapshot_manifests']={}
for base in sorted((Path.home()/'.cache/compat').glob('parity-baseline*')):
 p=base/'manifest.json'
 if not p.exists():continue
 m=load(p);hashes=m.get('sha256') or {k:v['sha256'] for k,v in m.items() if isinstance(v,dict) and 'sha256' in v}
 assert hashes and all(sha(base/k)==v for k,v in hashes.items()),str(base)
 summary['snapshot_manifests'][str(base)]=dict(files=len(hashes),sha256=sha(p))
paths=[Path(os.environ['SCENE_SETTINGS_CLI'])]
paths+=[p for p in (WORK/'managed-final').rglob('*') if p.is_file()]
paths+=list((WORK/'test-build').rglob('libunbound_physx.so'))
for p in paths:
 if p.name=='libunbound_physx.so':assert sha(p)=='4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208'
summary['artifacts_sha256']={str(p):sha(p) for p in paths}
summary['source_sha256']={str(p.relative_to(REPO)):sha(p) for p in [REPO/'managed/UnityEngine/Physics2D'/n for n in ['Collider2D.cs','Physics2DWorld.cs','Rigidbody2D.cs']] + [REPO/'managed/UnityEngine.Tests/Physics2DMotionParityTests.cs']}
summary['limits']=['Main 45-value motion objective is unresolved: bounce9, stack27, kinematic9 remain.','All 76 original query failures are preserved. No query candidate was merged.','Rejected contact/solver candidates remain archived; none are production source.','Native observations apply to Unity 6000.0.71f1 Linux only. Older releases and non-Box skin transitions remain unmeasured.','Root owns final integrated API verification. No full-engine parity claim.']
summary['evidence_sha256']={str(p.relative_to(DEST)):sha(p) for p in sorted(DEST.rglob('*')) if p.is_file()}
(DEST/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print('Verified all final gates; broad retains 121 failures, introduced 0; tests430',flush=True)
