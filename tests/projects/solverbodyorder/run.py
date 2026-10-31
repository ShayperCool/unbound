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
from probe_paths import cache_artifact_path, editor_path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['prepare', 'native', 'engine'])
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--label', required=True)
p.add_argument('--project', default='native1', help='Prepared project label for engine runs')
p.add_argument('--runtime', type=Path, help='Private frozen CLI and rebuilt managed directory')
a = p.parse_args()
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
    shutil.copy2(ROOT/'Sources/ProbeEntry.cs', project/'Assets/Scripts/ProbeEntry.cs')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')
    inputs = {str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest()
              for d in ['Assets', 'Packages', 'ProjectSettings'] for f in sorted((project/d).rglob('*')) if f.is_file()}
    (work/(a.label+'-inputs.json')).write_text(json.dumps(inputs, indent=2)+'\n')
elif a.mode == 'native':
    out = work/(a.label+'.json')
    log = work/(a.label+'.log')
    unity_log = work/(a.label+'-unity.log')
    audit = work/(a.label+'-command.json')
    if any(path.exists() for path in [out, log, unity_log, audit]):
        p.error('Refusing to overwrite '+a.label)
    env = dict(os.environ, PHYSICS_PROBE_OUT=str(out))
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600',
               os.environ.get('UNITY_EDITOR', str(editor_path())), '-batchmode', '-nographics',
               '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record',
               '-logFile', str(unity_log)]
    audit.write_text(json.dumps(dict(command=command, project=str(project)), indent=2)+'\n')
    with log.open('x') as f:
        result = subprocess.run(command, env=env, stdout=f, stderr=subprocess.STDOUT)
    if result.returncode or not out.exists(): raise SystemExit('Run failed: '+str(log))
else:
    if not a.runtime: p.error('--runtime is required')
    project = work/('project-'+a.project)
    runtime = a.runtime.resolve()
    out = work/(a.label+'.json')
    log = work/(a.label+'.log')
    if out.exists() or log.exists(): p.error('Refusing to overwrite '+a.label)
    env = dict(os.environ, PHYSICS_PROBE_OUT=str(out), UNBOUND_MANAGED_DIR=str(runtime/'managed'),
               LD_LIBRARY_PATH=str(runtime/'managed'))
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600', str(runtime/'unbound'),
               'run', '-p', str(project), '--headless', '30000', '--no-watch']
    audit = dict(command=command, sha256={str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in
        [runtime/'unbound', runtime/'managed/UnityEngine.CoreModule.dll', runtime/'managed/libunbound_physx.so']})
    (work/(a.label+'-command.json')).write_text(json.dumps(audit, indent=2)+'\n')
    with log.open('w') as f: result = subprocess.run(command, env=env, stdout=f, stderr=subprocess.STDOUT)
    if result.returncode or not out.exists(): raise SystemExit('Run failed: '+str(log))
