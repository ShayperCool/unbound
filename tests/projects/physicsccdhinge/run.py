#!/usr/bin/env python3
"""Record and replay CCD, hinge and backface contracts in private Unity projects."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import cache_artifact_path, reference_file, reference_root
from editor_runner import EditorJob, run, publish
sys.path.insert(0, str(REPO/'tests/projects/physicsbatch'))
from compare import load, compare as compare_rows
from verify_results import verify

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['prepare', 'native', 'engine', 'publish', 'compare', 'gate'])
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, default=Path.home()/'.cache/unbound/validation/runs/physicsccdhinge')
p.add_argument('--label', default='engine')
p.add_argument('--repeat-label')
p.add_argument('--cli', type=Path)
p.add_argument('--geometry', action='store_true', help='Extended mesh, compound and terrain CCD cases')
a = p.parse_args()
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
work = cache_artifact_path(a.work.resolve()) / ('geometry' if a.geometry else '')
work.mkdir(parents=True, exist_ok=True)
if Path(a.label).name != a.label or a.label in ('.','..'): p.error('Invalid label')
project = work/('project-'+a.label)
out = work/(a.label+'.tsv')
ref = reference_root(ROOT)/'geometry.tsv' if a.geometry else reference_file(ROOT, 'tsv')
output_variable = 'PHYSICS_GEOMETRY_OUT' if a.geometry else 'PHYSICS_PROBE_OUT'

def prepare():
    if project.exists(): raise SystemExit('Project exists: '+str(project))
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(REPO/'tests/projects/physics'/folder, project/folder)
    shutil.rmtree(project/'Assets/Scripts')
    shutil.copytree(ROOT/'Sources', project/'Assets/Scripts')
    manifest = project/'Packages/manifest.json'
    packages = json.loads(manifest.read_text())
    packages['dependencies'].update({'com.unity.modules.terrain':'1.0.0', 'com.unity.modules.terrainphysics':'1.0.0'})
    manifest.write_text(json.dumps(packages,indent=2)+'\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')

def engine():
    if not a.cli: p.error('--cli required')
    with (work/(a.label+'-engine.log')).open('w') as log:
        subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'),str(a.cli.resolve()),'run','-p',str(project),'--headless','30000','--no-watch'],env=dict(os.environ,**{output_variable:str(out)}),stdout=log,stderr=subprocess.STDOUT,check=True)

def compare(classify=False):
    expected,actual=load(ref),load(out)
    report=compare_rows(expected,actual)
    (work/(a.label+'-comparison.json')).write_text(json.dumps(report,indent=2)+'\n')
    print(f"physicsccdhinge: {report['total']-len(report['failures'])}/{report['total']} records match; {len(report['failures'])} differences")
    if not classify or a.geometry: return bool(report['failures'])
    result = verify(expected,actual,report)
    (work/(a.label+'-verification.json')).write_text(json.dumps(result,indent=2)+'\n')
    print(f"physicsccdhinge: {len(result['unresolved'])} unresolved; {len(result['angle_roundoff'])} angle roundoff; {len(result['resting_velocity_roundoff'])} resting velocity roundoff; {len(result['pair_orientation'])} pair orientation rows retained")
    return bool(result['unresolved'])

if a.mode=='prepare':prepare()
elif a.mode=='native':
    if out.exists():p.error('Capture exists')
    result=run(EditorJob(project,work/(a.label+'-unity.log'),work/(a.label+'-editor'),args=['-executeMethod','ProbeRunner.Record'],graphics=True,batch=True,env={output_variable:str(out)},timeout=600,launch_inputs={'release':a.release,'paths':{'sources':str(ROOT/'Sources'),'assets':str(project/'Assets')}}))
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
    publish(directories,ref,lambda directory:['capture.tsv'],write,complete=[complete],gpu=True)
elif a.mode=='engine':engine()
elif a.mode=='compare':raise SystemExit(compare())
elif a.mode=='gate':
    if project.exists():shutil.rmtree(project)
    prepare();engine();raise SystemExit(compare(classify=True))
