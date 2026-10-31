#!/usr/bin/env python3
"""Independent, append-only lifecycle skin captures in private project copies."""
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
work = Path(os.environ['SKIN_WORK'])
project = work / ('project-' + args.label)
if args.mode == 'prepare':
    if project.exists():
        raise SystemExit('refusing to overwrite ' + str(project))
    for folder in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(REPO / 'tests/projects/physics' / folder, project / folder)
    shutil.copy2(ROOT / 'Sources/ProbeEntry.cs', project / 'Assets/Scripts/ProbeEntry.cs')
else:
    output = work / (args.label + '.json')
    log = work / (args.label + '.log')
    if output.exists() or log.exists():
        raise SystemExit('refusing to overwrite capture/log')
    env = dict(os.environ, PHYSICS_PROBE_OUT=str(output))
    if args.mode == 'native':
        command = [str(Path.home() / 'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'),
                   '-batchmode', '-nographics', '-projectPath', str(project),
                   '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work / (args.label + '-editor.log'))]
    else:
        command = [os.environ['SCENE_SETTINGS_CLI'], 'run', '-p', str(project), '--headless', '30000', '--no-watch']
    with log.open('w') as handle:
        subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '300', *command],
                       env=env, check=True, stdout=handle, stderr=subprocess.STDOUT)
    assert output.is_file(), str(output)
    print(args.label, 'complete', flush=True)
