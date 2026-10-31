#!/usr/bin/env python3
"""Prepare private controls, capture Native order, or run a selected private runtime."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['prepare', 'native', 'engine'])
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--label', required=True)
p.add_argument('--project', default='native1', help='Prepared project label for engine runs')
p.add_argument('--runtime', type=Path, help='Private frozen CLI and rebuilt managed directory')
p.add_argument('--managed-trace', action='store_true', help='Record actual managed proxy IDs and edge order')
p.add_argument('--enumeration', action='store_true', help='Run the independent public GetContacts controls')
p.add_argument('--velocities', action='store_true', help='Record contact update order and reported velocities')
p.add_argument('--lifetimes', action='store_true', help='Record simultaneous pair loss and creation, including callback getters')
p.add_argument('--rotation-inputs', action='store_true', help='Measure the body rotation inputs used by contact geometry')
p.add_argument('--unattached-statics', action='store_true', help='Measure the shared scene body and proxy lifetime of unattached colliders')
p.add_argument('--contact-inputs', action='store_true', help='Preserve query controls while reading Native primitive contact inputs')
p.add_argument('--motion-witness', action='store_true', help='Preserve the broad prefix through the first four-body motion stack')
p.add_argument('--motion-through', help='Preserve the original motion sequence through this declared scenario')
a = p.parse_args()
if sum([a.enumeration, a.velocities, a.lifetimes, a.rotation_inputs, a.unattached_statics, a.contact_inputs, a.motion_witness, bool(a.motion_through)]) > 1: p.error('Select one control set')
motion_cases = ['original/'+kind for kind in ['bounce', 'stack', 'kinematic']]+[
    'solver/'+str(dt)+'/'+str(vi)+'/'+str(angle)
    for dt in [.01, .02, .04] for vi in [2, 8, 16] for angle in [0, 7]]
if a.motion_through and a.motion_through not in motion_cases: p.error('Unknown motion scenario')
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
work = a.work.resolve()
try:
    cache_artifact_path(work)
except ValueError as error:
    p.error(str(error))
if any(Path(s).name != s or s in ['.', '..'] for s in [a.label, a.project]): p.error('Invalid label')
work.mkdir(parents=True, exist_ok=True)
project = work/('project-'+a.label)
if a.mode == 'prepare':
    if project.exists(): p.error('Refusing to overwrite '+str(project))
    for folder in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(REPO/'tests/projects/physics'/folder, project/folder)
    for source in (ROOT/'Sources').glob('*.cs'):
        shutil.copy2(source, project/'Assets/Scripts'/source.name)
    if a.unattached_statics:
        shutil.copy2(ROOT/'Sources/UnattachedStatics/ProbeEntry.cs', project/'Assets/Scripts/ProbeEntry.cs')
    if a.contact_inputs:
        shutil.copy2(ROOT/'Sources/ContactInputs/ProbeEntry.cs', project/'Assets/Scripts/ProbeEntry.cs')
    if a.motion_witness or a.motion_through:
        source = (ROOT.parent/'physics2dmotion/Sources/ProbeEntry.cs').read_text()
        if a.motion_witness:
            start = source.index('        foreach (var dt in new[]')
            end = source.index('        File.WriteAllText(output, ProbeLog.Json());', start)
            source = source[:start]+source[end:]
            source = source.replace('new[] { "bounce", "stack", "kinematic" }', 'new[] { "bounce", "stack" }')
        else:
            # Finish the requested case, including teardown. Later declared
            # cases do nothing; every earlier simulation and observation stays.
            source = source.replace('    static float Dt;', '    static float Dt;\n    static bool Done;')
            source = source.replace('    public static void Snapshot(string key)\n    {',
                '    public static void Snapshot(string key)\n    {\n'
                '        ManagedTrace.RecordScenePairs(Step, "motion", key);\n'
                '        ManagedTrace.RecordState(Step, key, Bodies);\n'
                '        ManagedTrace.Record(Step, key, Bodies);')
            source = source.replace('        ProbeLog.Begin(name); Step = Event = 0;',
                '        if (Done) return;\n        bool traceCase = Environment.GetEnvironmentVariable("SOLVER_PAIR_QUERY_CASE") == name;\n'
                '        if (traceCase) Physics2D.velocityIterations = 63;\n'
                '        ProbeLog.Begin(name); Step = Event = 0;')
            source = source.replace('        Phase = "teardown";',
                '        if (traceCase) Physics2D.velocityIterations = 62;\n        Phase = "teardown";')
            source = source.replace('        Objects.Clear(); Bodies.Clear();',
                '        Objects.Clear(); Bodies.Clear();\n        if (name == '+json.dumps(a.motion_through)+') Done = true;')
        (project/'Assets/Scripts/ProbeEntry.cs').write_text(source)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')
    inputs = {str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest()
              for d in ['Assets', 'Packages', 'ProjectSettings'] for f in sorted((project/d).rglob('*')) if f.is_file()}
    (work/(a.label+'-inputs.json')).write_text(json.dumps(inputs, indent=2)+'\n')
elif a.mode == 'native':
    # This driver wraps the Editor once. It also captures public output in the same run.
    env = dict(os.environ, DYNAMICS_TRACE_STORAGE='1', UseSharedCompilation='false',
               MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
    if a.enumeration: env['SOLVER_PAIR_ENUMERATION'] = '1'
    if a.velocities: env['SOLVER_PAIR_VELOCITIES'] = '1'
    if a.lifetimes: env['SOLVER_PAIR_LIFETIMES'] = '1'
    if a.rotation_inputs: env['SOLVER_PAIR_ROTATION_INPUTS'] = '1'
    if a.unattached_statics: env.pop('DYNAMICS_TRACE_STORAGE', None)
    query_case = os.environ.get('SOLVER_PAIR_QUERY_CASE')
    subprocess.run([sys.executable, str(ROOT.parent/'physics2ddynamics/trace.py'), 'capture',
                    '--project', str(project), '--work', str(work/'traces'), '--label', a.label, '--pairs']+
                   (['--updates'] if a.velocities or a.unattached_statics or a.motion_witness or a.motion_through else [])+
                   (['--contact-inputs'] if a.contact_inputs else [])+
                   (['--queries', '--query-case', query_case] if query_case else []), env=env, check=True)
else:
    if not a.runtime: p.error('--runtime is required')
    project = work/('project-'+a.project)
    runtime = a.runtime.resolve()
    out = work/(a.label+'.json')
    log = work/(a.label+'.log')
    if out.exists() or log.exists(): p.error('Refusing to overwrite '+a.label)
    env = dict(os.environ, PHYSICS_PROBE_OUT=str(out), UNBOUND_MANAGED_DIR=str(runtime/'managed'),
               LD_LIBRARY_PATH=str(runtime/'managed'), UseSharedCompilation='false',
               MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
    if a.enumeration: env['SOLVER_PAIR_ENUMERATION'] = '1'
    if a.velocities: env['SOLVER_PAIR_VELOCITIES'] = '1'
    if a.lifetimes: env['SOLVER_PAIR_LIFETIMES'] = '1'
    if a.rotation_inputs: env['SOLVER_PAIR_ROTATION_INPUTS'] = '1'
    if a.managed_trace:
        trace = work/(a.label+'-managed.jsonl')
        if trace.exists(): p.error('Refusing to overwrite '+str(trace))
        env['SOLVER_PAIR_MANAGED_OUT'] = str(trace)
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600', str(runtime/'unbound'),
               'run', '-p', str(project), '--headless', '30000', '--no-watch']
    audit = dict(command=command, sha256={str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in
        [runtime/'unbound', runtime/'managed/UnityEngine.CoreModule.dll', runtime/'managed/libunbound_physx.so']})
    audit['environment'] = {k: env[k] for k in ['SOLVER_PAIR_ENUMERATION', 'SOLVER_PAIR_VELOCITIES',
        'SOLVER_PAIR_LIFETIMES', 'SOLVER_PAIR_ROTATION_INPUTS', 'SOLVER_PAIR_MANAGED_OUT',
        'SOLVER_PAIR_MANAGED_SHAPES'] if k in env}
    (work/(a.label+'-command.json')).write_text(json.dumps(audit, indent=2)+'\n')
    with log.open('w') as f: result = subprocess.run(command, env=env, stdout=f, stderr=subprocess.STDOUT)
    if result.returncode or not out.exists(): raise SystemExit('Run failed: '+str(log))
