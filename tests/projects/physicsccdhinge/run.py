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
from verify_geometry import verify as verify_geometry
from verify_ordering import verify as verify_ordering
from analyze_contacts import analyze
from replay_compound_order import diagnose

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['prepare', 'native', 'engine', 'publish', 'compare', 'gate'])
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, default=Path.home()/'.cache/unbound/validation/runs/physicsccdhinge')
p.add_argument('--label', default='engine')
p.add_argument('--repeat-label')
p.add_argument('--extend-reference', action='store_true', help='Append controls only if every existing value is unchanged')
p.add_argument('--cli', type=Path)
suite = p.add_mutually_exclusive_group()
suite.add_argument('--geometry', action='store_true', help='Extended mesh, compound and terrain CCD cases')
suite.add_argument('--geometry-controls', action='store_true', help='Dynamic target and mesh type transition controls')
suite.add_argument('--geometry-ordering', action='store_true', help='Actor, child and native contact orientation controls')
a = p.parse_args()
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
variant = 'geometry-ordering' if a.geometry_ordering else 'geometry-controls' if a.geometry_controls else 'geometry' if a.geometry else ''
work = cache_artifact_path(a.work.resolve()) / variant
work.mkdir(parents=True, exist_ok=True)
if Path(a.label).name != a.label or a.label in ('.','..'): p.error('Invalid label')
project = work/('project-'+a.label)
out = work/(a.label+'.tsv')
ref = reference_root(ROOT)/(variant+'.tsv') if variant else reference_file(ROOT, 'tsv')
output_variable = 'PHYSICS_GEOMETRY_ORDER_OUT' if a.geometry_ordering else 'PHYSICS_GEOMETRY_CONTROL_OUT' if a.geometry_controls else 'PHYSICS_GEOMETRY_OUT' if a.geometry else 'PHYSICS_PROBE_OUT'

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
        subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'),'--kind','engine-cpu',str(a.cli.resolve()),'run','-p',str(project),'--headless','30000','--no-watch'],env=dict(os.environ,**{output_variable:str(out)}),stdout=log,stderr=subprocess.STDOUT,check=True)

def compare(classify=False):
    expected,actual=load(ref),load(out)
    report=compare_rows(expected,actual)
    (work/(a.label+'-comparison.json')).write_text(json.dumps(report,indent=2)+'\n')
    print(f"physicsccdhinge: {report['total']-len(report['failures'])}/{report['total']} records match; {len(report['failures'])} differences")
    if not classify: return bool(report['failures'])
    if a.geometry_ordering:
        result = verify_ordering(expected, actual)
        (work/(a.label+'-verification.json')).write_text(json.dumps(result,indent=2)+'\n')
        print(f"physicsccdhinge: ordering strict report: {len(report['failures'])} differences; {len(result['unresolved'])} capture/native consistency failures; {len(result.get('large_coordinate_trace', []))} far-origin trace differences retained")
        return bool(result['unresolved'])
    if a.geometry:
        analysis = analyze(expected, actual)
        (work/(a.label+'-contact-analysis.json')).write_text(json.dumps(analysis,indent=2)+'\n')
        print(f"physicsccdhinge: contact analysis: {analysis['counts']}")
        replay = diagnose(expected, actual)
        (work/(a.label+'-compound-order-replay.json')).write_text(json.dumps(replay,indent=2)+'\n')
        print(f"physicsccdhinge: compound normal-response replay: reference={replay['reference_reconstructed']}; engine={replay['engine_reconstructed']}; reference response from engine contacts={replay['reference_response_from_engine_contacts']}")
        result = verify_geometry(expected,actual,report)
        (work/(a.label+'-verification.json')).write_text(json.dumps(result,indent=2)+'\n')
        print(f"physicsccdhinge: geometry motion gate: {result['checked_body_rows']} body rows; {len(result['unresolved'])} unexpected differences; contact trace remains a report with {len(result['contact_trace'])} raw differences; {len(result['mirrored_motion'])} reflected body rows; {len(result['open_manifold_motion'])} open mixed-compound rows")
        return bool(result['unresolved'])
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
    target = work/(a.label+'-validated-reference.tsv') if a.extend_reference else ref
    publish(directories,target,lambda directory:['capture.tsv'],write,complete=[complete],gpu=True)
    if a.extend_reference:
        previous,extended=load(ref),load(target)
        if any(extended.get(key)!=value for key,value in previous.items()):
            raise SystemExit('Extension would change an existing reference value')
        shutil.copy2(target,ref)
elif a.mode=='engine':engine()
elif a.mode=='compare':raise SystemExit(compare())
elif a.mode=='gate':
    if project.exists():shutil.rmtree(project)
    prepare();engine();raise SystemExit(compare(classify=True))
