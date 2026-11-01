#!/usr/bin/env python3
"""Run one release's offline source activity control in a private project."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

fixture = Path(__file__).resolve().parent
sys.path.insert(0, str(fixture.parents[1]))
from editor_runner import EditorJob, run as run_editor
sys.path.insert(0, str(fixture.parent / 'mixersuspend'))
from pack import pack
from verify_activity import verify

p = argparse.ArgumentParser()
p.add_argument('backend', choices=('native', 'engine'))
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
p.add_argument('--runtime', type=Path)
p.add_argument('--native-project', type=Path)
a = p.parse_args()
assert (fixture / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] == 'm_EditorVersion: ' + a.release
if a.backend == 'engine' and (not a.runtime or not a.native_project):
    p.error('engine requires --runtime and --native-project')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work / 'project'
source = fixture if a.backend == 'native' else a.native_project
for name in ('Assets', 'Packages', 'ProjectSettings'):
    shutil.copytree(source / name, project / name)
output = a.work / 'capture'
env = dict(os.environ, GENERATOR_LIFECYCLE_OUT=str(output), UseSharedCompilation='false',
           MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
heavy = str(Path.home() / '.cache/compat/with-heavy-job')
commands = []
if a.backend == 'native':
    editor = a.editor or Path.home() / 'Unity/Hub/Editor' / a.release / 'Editor/Unity'
    for stage in ('Prepare', 'Record'):
        job = EditorJob(project, a.work / (stage + '.log'), a.work / (stage + '-editor'),
                        args=['-executeMethod', 'ProbeRunner.' + stage], batch=True,
                        graphics=False, editor=editor, env=env, timeout=300,
                        progress_files=[output / 'capture.json', output],
                        progress_timeout=240 if stage == 'Record' else None)
        commands.append(job.command())
        (a.work / 'commands.json').write_text(json.dumps(commands, indent=2) + '\n')
        result = run_editor(job)
        if result.get('exit_code') != 0 or result.get('forced_exit'):
            raise SystemExit('Native ' + stage + ' failed: ' + json.dumps(result))
else:
    env.update(GENERATOR_LIFECYCLE_ENGINE='1', UNBOUND_CACHE=str(a.work / 'cache'),
               UNBOUND_MANAGED_DIR=str(a.runtime / 'managed'), LD_LIBRARY_PATH=str(a.runtime / 'managed'))
    artifacts = [a.runtime / 'unbound', *sorted((a.runtime / 'managed').rglob('*'))]
    runtime_hashes = {str(f.relative_to(a.runtime)): hashlib.sha256(f.read_bytes()).hexdigest() for f in artifacts if f.is_file()}
    assert runtime_hashes == json.loads((a.runtime / 'runtime-manifest.json').read_text()), 'Runtime manifest mismatch'
    (a.work / 'runtime-hashes.json').write_text(json.dumps(runtime_hashes, indent=2) + '\n')
    commands.append([heavy, 'timeout', '--kill-after=15', '300', str(a.runtime / 'unbound'), 'run', '-p', str(project),
                     '--scene', 'Assets/Scenes/Probe.unity', '--headless', '100', '--no-watch', '--fixed-fps', '60'])
(a.work / 'commands.json').write_text(json.dumps(commands, indent=2) + '\n')
for i, command in enumerate(commands if a.backend == 'engine' else []):
    with (a.work / ('stdout-' + str(i) + '.log')).open('x') as log:
        subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
if a.backend == 'engine':
    assert runtime_hashes == {str(f.relative_to(a.runtime)): hashlib.sha256(f.read_bytes()).hexdigest() for f in artifacts if f.is_file()}, 'Runtime mutated during capture'
(a.work / 'input-hashes.json').write_text(json.dumps({str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted((project / 'Assets').rglob('*')) if f.is_file()}, indent=2) + '\n')
verify(output, a.release)
pack(output)
