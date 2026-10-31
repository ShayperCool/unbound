#!/usr/bin/env python3
"""Independent, append-only lifecycle skin captures in private project copies."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_file, run_root

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('mode', choices=['prepare', 'native', 'engine', 'record-reference'])
parser.add_argument('--label', required=True)
parser.add_argument('--repeat-label')
args = parser.parse_args()
work = Path(os.environ.get('SKIN_WORK',str(run_root(ROOT))))
work.mkdir(parents=True,exist_ok=True)
project = work / ('project-' + args.label)
if args.mode == 'record-reference':
    if not args.repeat_label:
        parser.error('record-reference requires --repeat-label')
    first,second=work/(args.label+'.json'),work/(args.repeat_label+'.json')
    if first.read_bytes()!=second.read_bytes():
        raise SystemExit('Independent Native recordings differ')
    target=reference_file(ROOT);target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(first,target)
elif args.mode == 'prepare':
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
        command = [str(editor_path()),
                   '-batchmode', '-nographics', '-projectPath', str(project),
                   '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work / (args.label + '-editor.log'))]
    else:
        command = [os.environ['SCENE_SETTINGS_CLI'], 'run', '-p', str(project), '--headless', '30000', '--no-watch']
    with log.open('w') as handle:
        subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '300', *command],
                       env=env, check=True, stdout=handle, stderr=subprocess.STDOUT)
    assert output.is_file(), str(output)
    print(args.label, 'complete', flush=True)
