#!/usr/bin/env python3
"""Prepare private projects and capture a release-selected force-field probe."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['prepare', 'native', 'engine'])
p.add_argument('--unity-version', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--label', required=True)
p.add_argument('--cli', type=Path)
p.add_argument('--runtime', type=Path)
a = p.parse_args()
work = a.work.resolve()
work.mkdir(parents=True, exist_ok=True)
project = work / ('project-' + a.label)
if a.mode == 'prepare':
    if project.exists(): raise SystemExit('Refusing to overwrite ' + str(project))
    for folder in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(ROOT.parent / 'particles' / folder, project / folder)
    shutil.rmtree(project / 'Assets/Scripts')
    (project / 'Assets/Scripts').mkdir()
    shutil.copy2(ROOT / 'Sources/ForceFieldProbe.cs', project / 'Assets/Scripts/ForceFieldProbe.cs')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + a.unity_version + '\n')
    files = {str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest()
             for d in ['Assets', 'Packages', 'ProjectSettings'] for f in sorted((project/d).rglob('*')) if f.is_file()}
    (work / (a.label + '-inputs.json')).write_text(json.dumps(files, indent=2) + '\n')
else:
    out = work / (a.label + '.jsonl')
    log = work / (a.label + '.log')
    if out.exists() or log.exists(): raise SystemExit('Refusing to overwrite ' + a.label)
    env = dict(os.environ, PARTICLE_FIELD_OUT=str(out))
    if a.mode == 'native':
        editor = os.environ.get('UNBOUND_PROBE_UNITY', str(Path.home()/'Unity/Hub/Editor'/a.unity_version/'Editor/Unity'))
        cmd = [editor, '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work/(a.label+'-editor.log'))]
    else:
        if not a.cli or not a.runtime: p.error('engine requires --cli and --runtime')
        env.update(UNBOUND_MANAGED_DIR=str(a.runtime.resolve()), LD_LIBRARY_PATH=str(a.runtime.resolve()))
        cmd = [str(a.cli.resolve()), 'run', '-p', str(project), '--headless', '30', '--no-watch']
    cmd = [str(Path.home()/'.cache/compat/with-heavy-job'), *cmd]
    audit = dict(command=cmd, environment={k:env.get(k) for k in ['PARTICLE_FIELD_OUT','UNBOUND_MANAGED_DIR','LD_LIBRARY_PATH']})
    if a.mode == 'engine':
        audit['sha256'] = {str(f): hashlib.sha256(f.read_bytes()).hexdigest() for f in [a.cli, a.runtime/'UnityEngine.CoreModule.dll', a.runtime/'libunbound_physx.so']}
    (work / (a.label + '-command.json')).write_text(json.dumps(audit, indent=2) + '\n')
    with log.open('x') as f: result = subprocess.run(cmd, env=env, stdout=f, stderr=subprocess.STDOUT)
    print(a.label, 'exit', result.returncode)
    if result.returncode or not out.exists(): raise SystemExit('Capture failed: ' + str(log))
