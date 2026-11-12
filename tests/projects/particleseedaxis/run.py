#!/usr/bin/env python3
"""Prepare private projects and record pre-simulation seed/axis controls."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path
import editor_runner  # noqa: E402


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=['prepare', 'native', 'engine'])
    p.add_argument('--unity-version', required=True)
    p.add_argument('--work', type=Path, required=True)
    p.add_argument('--label', required=True)
    p.add_argument('--cli', type=Path)
    p.add_argument('--runtime', type=Path)
    a = p.parse_args()
    if not a.label or Path(a.label).name != a.label or a.label in ['.', '..']:
        p.error('labels must be single directory names')
    try:
        cache_artifact_path(a.work)
    except ValueError as error:
        p.error(str(error))
    work = a.work.resolve()
    work.mkdir(parents=True, exist_ok=True)
    project = work / ('project-' + a.label)
    if a.mode == 'prepare':
        if project.exists():
            raise SystemExit('Refusing to overwrite ' + str(project))
        for folder in ['Assets', 'Packages', 'ProjectSettings']:
            shutil.copytree(ROOT.parent/'particles'/folder, project/folder)
        shutil.rmtree(project/'Assets/Scripts')
        (project/'Assets/Scripts').mkdir()
        shutil.copy2(ROOT/'Sources/SeedAxisProbe.cs', project/'Assets/Scripts/SeedAxisProbe.cs')
        (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + a.unity_version + '\n')
        files = {str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest()
                 for d in ['Assets', 'Packages', 'ProjectSettings']
                 for f in sorted((project/d).rglob('*')) if f.is_file()}
        (work/(a.label+'-inputs.json')).write_text(json.dumps(files, indent=2)+'\n')
        return
    out, log = work/(a.label+'.jsonl'), work/(a.label+'.log')
    if out.exists() or log.exists():
        raise SystemExit('Refusing to overwrite ' + a.label)
    env = dict(os.environ, PARTICLE_SEED_AXIS_OUT=str(out),
               UNBOUND_CACHE=str(work/'engine-cache'), CARGO_TARGET_DIR=str(work/'target'),
               DOTNET_CLI_HOME=str(work/'dotnet-home'), DOTNET_CLI_USE_MSBUILD_SERVER='0',
               MSBUILDDISABLENODEREUSE='1', UseSharedCompilation='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1')
    if a.mode == 'native':
        # A batchmode Editor on the GPU (tests/editor_runner.py takes its own launcher lease).
        editor = os.environ.get('UNBOUND_PROBE_UNITY', str(Path.home()/'Unity/Hub/Editor'/a.unity_version/'Editor/Unity'))
        job = editor_runner.EditorJob(project, work/(a.label+'-editor.log'), work/(a.label+'-editor'),
                                      ['-executeMethod', 'ProbeRunner.Record'], batch=True, graphics=True,
                                      env={'PARTICLE_SEED_AXIS_OUT': str(out)}, timeout=1200, editor=editor,
                                      label='particleseedaxis-'+a.label)
        result = editor_runner.run(job)
        print(editor_runner.summary(result))
        if result.get('exit_code') or result.get('forced_exit') or not out.exists():
            raise SystemExit('Capture failed: ' + str(job.log))
        return
    else:
        if not a.cli or not a.runtime:
            p.error('engine requires --cli and --runtime')
        env.update(UNBOUND_MANAGED_DIR=str(a.runtime.resolve()), LD_LIBRARY_PATH=str(a.runtime.resolve()))
        command = [str(a.cli.resolve()), 'run', '-p', str(project), '--headless', '30', '--no-watch']
    # The installed admission wrapper is entered here exactly once. Do not wrap this runner.
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600', *command]
    audit = dict(command=command, environment={k: v for k, v in env.items() if k in (
        'PARTICLE_SEED_AXIS_OUT', 'UNBOUND_CACHE', 'CARGO_TARGET_DIR', 'DOTNET_CLI_HOME',
        'DOTNET_CLI_USE_MSBUILD_SERVER', 'MSBUILDDISABLENODEREUSE', 'UseSharedCompilation',
        'UNBOUND_MANAGED_DIR', 'LD_LIBRARY_PATH')})
    if a.mode == 'engine':
        audit['sha256'] = {str(f): hashlib.sha256(f.read_bytes()).hexdigest() for f in
                           [a.cli, a.runtime/'UnityEngine.CoreModule.dll', a.runtime/'libunbound_physx.so']}
    (work/(a.label+'-command.json')).write_text(json.dumps(audit, indent=2)+'\n')
    with log.open('x') as f:
        result = subprocess.run(command, env=env, stdout=f, stderr=subprocess.STDOUT)
    (work/(a.label+'-result.json')).write_text(json.dumps(dict(
        exit_code=result.returncode, output_exists=out.exists()), indent=2)+'\n')
    print(a.label, 'exit', result.returncode)
    if result.returncode or not out.exists():
        raise SystemExit('Capture failed: ' + str(log))


if __name__ == '__main__':
    main()
