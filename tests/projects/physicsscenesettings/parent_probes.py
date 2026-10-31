#!/usr/bin/env python3
"""Run focused integration probes in private copies, under the shared heavy-job lock."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess


ROOT = Path(__file__).resolve().parent
SOURCES = {'triggers': 'TriggerLifecycle.cs', 'barrier': 'RegistrationBarrier.cs',
           'kinematic': 'KinematicTransitions.cs', 'order': 'ContactOrder.cs',
           'component': 'ComponentBarriers.cs', 'flags': 'TriggerFlagLifecycle.cs'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['prepare', 'native', 'engine'])
    parser.add_argument('--probe', choices=SOURCES, required=True)
    parser.add_argument('--repeat', type=int, choices=[1, 2], default=1)
    parser.add_argument('--label', default='final')
    args = parser.parse_args()
    work = Path(os.environ['SCENE_PARENT_WORK']).resolve()
    project = work / args.probe
    if args.mode == 'prepare':
        for folder in ['Assets', 'Packages', 'ProjectSettings']:
            shutil.copytree(ROOT / folder, project / folder, dirs_exist_ok=True)
        shutil.copyfile(ROOT / 'ParentProbes' / SOURCES[args.probe],
                        project / 'Assets/Scripts/SceneSettingsProbe.cs')
        if args.probe == 'flags':
            settings = project / 'ProjectSettings/DynamicsManager.asset'
            settings.write_text(settings.read_text().replace('m_ContactPairsMode: 0', 'm_ContactPairsMode: 3'))
        return
    stem = f'{args.probe}-native{args.repeat}' if args.mode == 'native' else f'{args.probe}-{args.label}'
    output = work / (stem + '.jsonl')
    output.unlink(missing_ok=True)
    env = dict(os.environ, SCENE_SETTINGS_OUT=str(output))
    if args.probe in ['barrier', 'order', 'component']:
        env['SCENE_SETTINGS_NOISE'] = '1'
    if args.probe == 'barrier':
        env['SCENE_SETTINGS_CLONE_BARRIER'] = '1'
    if args.mode == 'native':
        command = [str(Path.home() / 'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'),
                   '-batchmode', '-nographics', '-projectPath', str(project),
                   '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work / (stem + '.log'))]
    else:
        command = [os.environ['SCENE_SETTINGS_CLI'], 'run', '-p', str(project),
                   '--headless', '30000', '--no-watch']
    with (work / (stem + '-stdout.log')).open('w') as log:
        result = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'),
                                 'timeout', '180', *command], env=env,
                                stdout=log, stderr=subprocess.STDOUT)
    if result.returncode or not output.exists():
        raise SystemExit(f'{stem}: exit {result.returncode}, output exists: {output.exists()}')
    print(stem, len(output.read_text().splitlines()), 'records')


if __name__ == '__main__':
    main()
