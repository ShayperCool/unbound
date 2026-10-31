#!/usr/bin/env python3
"""Record geometry frames in independent cache projects for an explicit release."""
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
sys.path.insert(0, str(REPO / 'tests'))
from probe_paths import editor_path, reference_file, validation_cache

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['prepare', 'native', 'engine', 'record-reference'])
p.add_argument('--unity-version', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--label', required=True)
p.add_argument('--repeat-label')
p.add_argument('--cli', type=Path)
a = p.parse_args()
if any(Path(label).name != label or label in ['.', '..'] for label in [a.label, a.repeat_label or a.label]):
    p.error('labels must be single directory names')
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.unity_version
work = a.work.resolve()
if not work.is_relative_to(validation_cache().resolve()):
    p.error('--work must be under the unbound cache')
work.mkdir(parents=True, exist_ok=True)
project = work / ('project-' + a.label)
if a.mode == 'prepare':
    if project.exists():
        p.error('refusing to replace project')
    for folder in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(REPO / 'tests/projects/physics' / folder, project / folder)
    shutil.copy2(ROOT / 'Sources/ProbeEntry.cs', project / 'Assets/Scripts/ProbeEntry.cs')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + a.unity_version + '\n')
    manifest = {str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest()
                for d in ['Assets', 'Packages', 'ProjectSettings']
                for f in sorted((project / d).rglob('*')) if f.is_file()}
    (work / (a.label + '-inputs.json')).write_text(json.dumps(manifest, indent=2) + '\n')
elif a.mode == 'record-reference':
    if not a.repeat_label or a.repeat_label == a.label:
        p.error('two independent labels required')
    first, second = [work / (label + '.json') for label in [a.label, a.repeat_label]]
    for label in [a.label, a.repeat_label]:
        audit = json.loads((work / (label + '-command.json')).read_text())
        if audit['mode'] != 'native' or audit['unity_version'] != a.unity_version:
            p.error('reference requires Native recordings of the selected release')
    if (work / (a.label + '-inputs.json')).read_bytes() != (work / (a.repeat_label + '-inputs.json')).read_bytes():
        p.error('independent authored inputs differ')
    if first.read_bytes() != second.read_bytes():
        p.error('independent Native recordings differ')
    target = reference_file(ROOT)
    if target.exists():
        p.error('refusing to replace canonical reference')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(first, target)
    print('Native recordings agree exactly:', hashlib.sha256(first.read_bytes()).hexdigest())
else:
    output, log, editor_log = [work / (a.label + suffix) for suffix in ['.json', '.log', '-editor.log']]
    if any(f.exists() for f in [output, log, editor_log]):
        p.error('refusing to replace capture or log')
    if a.mode == 'native':
        command = [str(editor_path()), '-batchmode', '-nographics', '-projectPath', str(project),
                   '-executeMethod', 'ProbeRunner.Record', '-logFile', str(editor_log)]
    else:
        if not a.cli:
            p.error('engine requires --cli')
        command = [str(a.cli.resolve()), 'run', '-p', str(project), '--headless', '30000', '--no-watch']
    command = [str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '600', *command]
    audit = dict(mode=a.mode, unity_version=a.unity_version, command=command,
                 environment={k: os.environ.get(k) for k in ['UNBOUND_MANAGED_DIR', 'LD_LIBRARY_PATH']})
    (work / (a.label + '-command.json')).write_text(json.dumps(audit, indent=2) + '\n')
    with log.open('x') as handle:
        subprocess.run(command, env=dict(os.environ, PHYSICS_PROBE_OUT=str(output)),
                       stdout=handle, stderr=subprocess.STDOUT, check=True)
    if not output.is_file():
        raise SystemExit('missing capture: ' + str(output))
    print(a.label + ' complete', flush=True)
