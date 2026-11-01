#!/usr/bin/env python3
"""Run the modules probe in the Unity Editor (native) or Unbound (engine).

Scenarios:
  compile  the project plus Variants/CompileErrors: scripts using the APIs of modules the
           manifest leaves out. Records which assemblies compile and every compiler error.
  play     the project as committed: Play mode in a scene whose components come from a module
           the manifest leaves out. Records what ModulesProbe writes.
  editor-play  (engine only) open the scene in Edit mode, then enter Play mode. Also checks
               that a user-assembly component with an engine type's name survives pruning.
  create   (native only) makes the probe scene with ProbeRunner.Create in a fresh copy.

Every run goes to <work>/<label> (write-once, under $XDG_CACHE_HOME/unbound) and ends with
result.json there.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path

ERROR = re.compile(r'^(Assets/[^(]+)\((\d+),(\d+)\): error (CS\d+): (.*)$')
# Unbound's diagnostics: `<path>(line,col): CSxxxx: message`.
ENGINE_ERROR = re.compile(r'^\s+(.+?)\((\d+),(\d+)\): (CS\d+): (.*)$')

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument('mode', choices=['native', 'engine'])
p.add_argument('scenario', choices=['compile', 'play', 'editor-play', 'create'])
p.add_argument('--release', required=True)
p.add_argument('--label', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--cli', type=Path)
p.add_argument('--runtime', type=Path)
a = p.parse_args()
if Path(a.label).name != a.label or a.label in ['', '.', '..']:
    p.error('label must be a single directory name')
if a.mode == 'engine' and a.scenario == 'create':
    p.error('create is Editor-only')
if a.mode == 'native' and a.scenario == 'editor-play':
    p.error('editor-play is an engine integration control of the existing Native play reference')
try:
    cache_artifact_path(a.work)
except ValueError as error:
    p.error(str(error))
work = a.work.resolve() / a.label
work.mkdir(parents=True, exist_ok=False)
project = work / 'project'
for directory in ['Assets', 'Packages', 'ProjectSettings']:
    shutil.copytree(ROOT / directory, project / directory)
if a.scenario == 'create':
    shutil.rmtree(project / 'Assets/Scenes', ignore_errors=True)
    (project / 'Assets/Scenes.meta').unlink(missing_ok=True)
if a.scenario == 'compile':
    shutil.copytree(ROOT / 'Variants/CompileErrors/Assets', project / 'Assets', dirs_exist_ok=True)
if a.scenario == 'editor-play':
    shutil.copytree(ROOT / 'Variants/EditorTransition/Assets', project / 'Assets', dirs_exist_ok=True)
(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + a.release + '\n')

output = work / 'probe.jsonl'
env = dict(os.environ, MODULES_PROBE_OUT=str(output))
if a.scenario == 'editor-play':
    env['MODULES_TRANSITION_OUT'] = str(work / 'transition.json')
log = work / 'editor.log'
if a.mode == 'native':
    editor = os.environ.get('UNBOUND_PROBE_UNITY', str(Path.home() / 'Unity/Hub/Editor' / a.release / 'Editor/Unity'))
    command = [editor, '-batchmode', '-nographics', '-projectPath', str(project), '-logFile', str(log)]
    command += {'compile': ['-quit'], 'create': ['-quit', '-executeMethod', 'ProbeRunner.Create'],
                'play': ['-executeMethod', 'ProbeRunner.Record']}[a.scenario]
else:
    if not a.cli or not a.runtime:
        p.error('engine requires a matching --cli and --runtime')
    env.update(UNBOUND_MANAGED_DIR=str(a.runtime.resolve()), LD_LIBRARY_PATH=str(a.runtime.resolve()))
    if a.scenario == 'compile':
        command = [str(a.cli.resolve()), 'compile', '-p', str(project), '--editor-assemblies']
    elif a.scenario == 'editor-play':
        command = [str(a.cli.resolve()), 'test', '-p', str(project), '--platform', 'EditMode',
                   '--results', str(work / 'unused-nunit.xml')]
    else:
        command = [str(a.cli.resolve()), 'run', '-p', str(project), '--headless', '30', '--no-watch']
kind = ['--kind', 'engine-cpu'] if a.scenario == 'editor-play' else []
command = [str(Path.home() / '.cache/compat/with-heavy-job'), *kind, 'timeout', '600', *command]
audit = {'command': command, 'scenario': a.scenario}
if a.mode == 'engine':
    audit['sha256'] = {str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                       for path in [a.cli, a.runtime / 'UnityEngine.CoreModule.dll', a.runtime / 'Unbound.Compiler.dll']
                       if path.exists()}
(work / 'command.json').write_text(json.dumps(audit, indent=2) + '\n')
with (work / 'stdout.log').open('x') as out:
    exit_code = subprocess.run(command, env=env, stdout=out, stderr=subprocess.STDOUT).returncode

result = {'scenario': 'play' if a.scenario == 'editor-play' else a.scenario, 'exitCode': exit_code}
if a.scenario == 'editor-play':
    result['execution'] = a.scenario
    transition = work / 'transition.json'
    result['transition'] = json.loads(transition.read_text()) if transition.exists() else None
if a.scenario == 'compile':
    errors = set()
    if a.mode == 'native':
        lines = log.read_text(errors='replace').splitlines()
        for line in lines:
            m = ERROR.match(line)
            if m:
                errors.add((m[1], int(m[2]), int(m[3]), m[4], m[5]))
        result['scriptsHaveCompilerErrors'] = any('Scripts have compiler errors' in line for line in lines)
        result['compiledAssemblies'] = sorted(f.stem for f in (project / 'Library/ScriptAssemblies').glob('*.dll'))
    else:
        compiled_status = {}
        current = None
        for line in (work / 'stdout.log').read_text(errors='replace').splitlines():
            if line.startswith('FAILED '):
                current = line.split()[1]
                compiled_status[current] = 'failed'
            elif line.startswith('skipped '):
                compiled_status[line.split()[1].rstrip(':')] = 'skipped'
            m = ENGINE_ERROR.match(line)
            if m:
                path = Path(m[1])
                relative = str(path.relative_to(project)) if path.is_absolute() and path.is_relative_to(project) else m[1]
                errors.add((relative, int(m[2]), int(m[3]), m[4], m[5]))
        result['notCompiled'] = compiled_status
        result['compiledAssemblies'] = sorted(d.name for d in (project / 'Library/Unbound/ScriptAssemblies').iterdir()
                                              if d.is_dir() and any(d.glob('*.dll')))
    result['errors'] = [dict(zip(['file', 'line', 'column', 'id', 'message'], e)) for e in sorted(errors)]
elif a.scenario in ['play', 'editor-play']:
    result['rows'] = [json.loads(line) for line in output.read_text().splitlines()] if output.exists() else None
else:
    result['created'] = sorted(str(f.relative_to(project)) for f in [*(project / 'Assets').rglob('*.meta'),
                                                                     project / 'Packages/packages-lock.json',
                                                                     project / 'ProjectSettings/EditorBuildSettings.asset']
                               if f.exists())
(work / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
print(work / 'result.json', flush=True)
if a.scenario == 'editor-play' and (exit_code or not result['transition'] or not all(result['transition'].values())):
    sys.exit('Edit-to-Play/assembly identity integration control failed; see result.json')
