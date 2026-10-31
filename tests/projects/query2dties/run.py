#!/usr/bin/env python3
"""Write-once query-feature controls in independent private project copies."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('mode', choices=['prepare', 'native', 'engine'])
parser.add_argument('--label', required=True)
args = parser.parse_args()
work = Path(os.environ['QUERY_TIES_WORK'])
project = work / ('project-' + args.label)
if args.mode == 'prepare':
    if project.exists():
        raise SystemExit('Refusing to replace project: ' + str(project))
    for folder in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(REPO / 'tests/projects/physics' / folder, project / folder)
    shutil.copy2(ROOT / 'Sources/ProbeEntry.cs', project / 'Assets/Scripts/ProbeEntry.cs')
else:
    output = work / (args.label + '.json')
    log = work / (args.label + '.log')
    editor_log = work / (args.label + '-editor.log')
    if output.exists() or log.exists() or editor_log.exists():
        raise SystemExit('Refusing to replace capture or log')
    if args.mode == 'native':
        command = [str(Path.home() / 'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'),
                   '-batchmode', '-nographics', '-projectPath', str(project),
                   '-executeMethod', 'ProbeRunner.Record', '-logFile', str(editor_log)]
    else:
        command = [os.environ['SCENE_SETTINGS_CLI'], 'run', '-p', str(project),
                   '--headless', '30000', '--no-watch']
    with log.open('x') as handle:
        subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'),
                        'timeout', '600', *command],
                       env=dict(os.environ, PHYSICS_PROBE_OUT=str(output)),
                       stdout=handle, stderr=subprocess.STDOUT, check=True)
    assert output.is_file(), output
    print(args.label + ' complete', flush=True)
