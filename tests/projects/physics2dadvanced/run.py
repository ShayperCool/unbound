#!/usr/bin/env python3
"""Record advanced 2D physics controls against an explicitly selected release."""
import argparse
import hashlib
import importlib.util
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
from editor_runner import EditorJob, run as run_editor

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['prepare', 'native', 'engine', 'compare'])
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--label', required=True)
p.add_argument('--project', help='Prepared project label for an engine replay')
p.add_argument('--runtime', type=Path)
p.add_argument('--candidate', type=Path)
p.add_argument('--baseline', type=Path)
p.add_argument('--reference', type=Path)
p.add_argument('--output', type=Path)
a = p.parse_args()
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
try:
    work = cache_artifact_path(a.work.resolve())
except ValueError as error:
    p.error(str(error))
if any(Path(value).name != value or value in ['.', '..'] for value in [a.label, a.project or a.label]):
    p.error('Invalid project or capture label')
work.mkdir(parents=True, exist_ok=True)
project = work/('project-'+(a.project or a.label))
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()

if a.mode == 'prepare':
    if project.exists(): p.error('Refusing to overwrite '+str(project))
    for folder in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(REPO/'tests/projects/physics'/folder, project/folder)
    shutil.copy2(ROOT/'Sources/ProbeEntry.cs', project/'Assets/Scripts/ProbeEntry.cs')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')
    (work/(a.label+'-inputs.json')).write_text(json.dumps({
        str(path.relative_to(project)):sha(path)
        for folder in ['Assets', 'Packages', 'ProjectSettings']
        for path in sorted((project/folder).rglob('*')) if path.is_file()}, indent=2)+'\n')
elif a.mode == 'compare':
    if not a.candidate or not a.output: p.error('--candidate and --output are required')
    cache_artifact_path(a.output)
    spec = importlib.util.spec_from_file_location('comparison', ROOT.parent/'trigger2dtiming/report.py')
    c = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(c)
    reference = a.reference or ROOT/'Reference'/('unity-'+a.release+'.json')
    expected = c.load(reference)
    report = c.compare(expected, c.load(a.candidate), 1e-3)
    if a.baseline: report = c.changes(c.compare(expected, c.load(a.baseline), 1e-3), report)
    report['inputs_sha256'] = {str(path.resolve()):sha(path)
        for path in [reference, a.candidate]+([a.baseline] if a.baseline else [])}
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(report, indent=2)+'\n')
    c.describe('Advanced 2D controls', report)
    raise SystemExit(bool(report['introduced'] if a.baseline else report['failures']))
else:
    out = cache_artifact_path(work/(a.label+'.json'))
    log = work/(a.label+'-driver.log')
    if out.exists() or log.exists(): p.error('Refusing to overwrite '+a.label)
    env = dict(os.environ, PHYSICS_PROBE_OUT=str(out), UseSharedCompilation='false',
        MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600']
    if a.mode == 'native':
        job = EditorJob(project, work/(a.label+'-unity.log'), work/(a.label+'-editor'),
                        args=['-executeMethod', 'ProbeRunner.Record'], graphics=True, batch=True,
                        env={'PHYSICS_PROBE_OUT': str(out)}, timeout=600,
                        progress_files=[out], progress_timeout=300)
        command = job.command()
        artifacts = {}
    else:
        if not a.runtime: p.error('--runtime is required')
        runtime = a.runtime.resolve()
        managed = Path(os.environ.get('UNBOUND_MANAGED_DIR') or (runtime/'managed' if (runtime/'managed').is_dir() else runtime.parent/'managed')).resolve()
        env.update(UNBOUND_MANAGED_DIR=str(managed), LD_LIBRARY_PATH=str(managed))
        command += [str(runtime/'unbound'), 'run', '-p', str(project), '--headless', '30000', '--no-watch']
        artifacts = {str(path):sha(path) for path in [runtime/'unbound',
            managed/'UnityEngine.CoreModule.dll', managed/'libunbound_physx.so']}
    audit = dict(command=command, release=a.release, artifacts_sha256=artifacts,
        source_sha256=sha(project/'Assets/Scripts/ProbeEntry.cs'),
        environment={key:value for key,value in env.items() if key in
            ['PHYSICS_PROBE_OUT','UNBOUND_MANAGED_DIR','LD_LIBRARY_PATH','UNBOUND_PROBE_UNITY_VERSION']})
    (work/(a.label+'-command.json')).write_text(json.dumps(audit, indent=2)+'\n')
    if a.mode == 'native':
        result = run_editor(job)
        log.write_text(json.dumps(result, indent=2)+'\n')
        failed = result['exit_code'] != 0 or bool(result.get('forced_exit'))
    else:
        with log.open('x') as stream:
            result = subprocess.run(command, env=env, stdout=stream, stderr=subprocess.STDOUT)
        failed = result.returncode != 0
    if failed or not out.exists(): raise SystemExit('Capture failed: '+str(log))
