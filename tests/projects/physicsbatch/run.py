#!/usr/bin/env python3
"""Record and replay 3D batch query contracts in private Unity projects."""
import argparse
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import cache_artifact_path, reference_file
from editor_runner import EditorJob, run, publish

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['prepare', 'native', 'engine', 'publish', 'compare', 'gate'])
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, default=Path.home()/'.cache/unbound/validation/runs/physicsbatch')
p.add_argument('--label', default='engine')
p.add_argument('--repeat-label')
p.add_argument('--cli', type=Path)
p.add_argument('--extend-reference', action='store_true')
a = p.parse_args()
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
work = cache_artifact_path(a.work.resolve())
work.mkdir(parents=True, exist_ok=True)
if Path(a.label).name != a.label or a.label in ('.','..'): p.error('Invalid label')
project = work/('project-'+a.label)
out = work/(a.label+'.tsv')
ref = reference_file(ROOT, 'tsv')

def prepare():
    if project.exists(): raise SystemExit('Project exists: '+str(project))
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(REPO/'tests/projects/physics'/folder, project/folder)
    shutil.copy2(ROOT/'Sources/ProbeEntry.cs', project/'Assets/Scripts/ProbeEntry.cs')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')

def engine():
    if not a.cli: p.error('--cli required')
    with (work/(a.label+'-engine.log')).open('w') as log:
        subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'),str(a.cli.resolve()),'run','-p',str(project),'--headless','30000','--no-watch'],env=dict(os.environ,PHYSICS_PROBE_OUT=str(out)),stdout=log,stderr=subprocess.STDOUT,check=True)

def compare():
    def load(path):
        result = {}
        for line in path.read_text().splitlines():
            k,v = line.split('\t',1)
            if k in result: raise ValueError('Duplicate key '+k)
            result[k]=v
        if result.get('complete') != 'yes' or 'fatal' in result: raise ValueError('Incomplete capture '+str(path))
        return result
    expected,actual=load(ref),load(out)
    failures=[]
    for key in sorted(expected.keys()|actual.keys()):
        x,y=expected.get(key),actual.get(key)
        equal=x==y
        if not equal and x is not None and y is not None:
            xx,yy=x.split(','),y.split(',')
            if len(xx)==len(yy):
                def same(v,w):
                    if v==w:return True
                    try:return math.isclose(float(v),float(w),abs_tol=1e-4,rel_tol=1e-5)
                    except ValueError:return False
                equal=all(same(v,w) for v,w in zip(xx,yy))
        if not equal:failures.append(dict(key=key,expected=x,actual=y))
    report=dict(total=len(expected.keys()|actual.keys()),failures=failures)
    (work/(a.label+'-comparison.json')).write_text(json.dumps(report,indent=2)+'\n')
    print(f"physicsbatch: {report['total']-len(failures)}/{report['total']} records match; {len(failures)} differences")
    return bool(failures)

if a.mode=='prepare':prepare()
elif a.mode=='native':
    if out.exists():p.error('Capture exists')
    result=run(EditorJob(project,work/(a.label+'-unity.log'),work/(a.label+'-editor'),args=['-executeMethod','ProbeRunner.Record'],graphics=True,batch=True,env={'PHYSICS_PROBE_OUT':str(out)},timeout=600,launch_inputs={'release':a.release,'paths':{'sources':str(ROOT/'Sources'),'assets':str(project/'Assets')}}))
    print(json.dumps(result))
    if result['exit_code']!=0:raise SystemExit(1)
elif a.mode=='publish':
    if not a.repeat_label:p.error('--repeat-label required')
    directories = [work/(label+'-editor') for label in (a.label,a.repeat_label)]
    for directory,label in zip(directories,(a.label,a.repeat_label)):
        shutil.copy2(work/(label+'.tsv'), directory/'capture.tsv')
    def complete(directory):
        rows=(directory/'capture.tsv').read_text().splitlines()
        return None if rows and rows[-1]=='complete\tyes' and not any(x.startswith('fatal\t') for x in rows) else 'Incomplete capture'
    def write(target,first):
        target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(first/'capture.tsv',target)
    target = work/(a.label+'-validated-reference.tsv') if a.extend_reference else ref
    publish(directories,target,lambda directory:['capture.tsv'],write,complete=[complete],gpu=True)
    if a.extend_reference:
        previous=dict(line.split('\t',1) for line in ref.read_text().splitlines())
        extended=dict(line.split('\t',1) for line in target.read_text().splitlines())
        if any(extended.get(key)!=value for key,value in previous.items()):
            raise SystemExit('Extension would change an existing reference value')
        shutil.copy2(target,ref)
elif a.mode=='engine':engine()
elif a.mode=='compare':raise SystemExit(compare())
elif a.mode=='gate':
    if project.exists():shutil.rmtree(project)
    prepare();engine();raise SystemExit(compare())
