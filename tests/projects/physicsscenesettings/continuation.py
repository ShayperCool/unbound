#!/usr/bin/env python3
"""Record cleanup-order controls in private Unity-compatible project copies."""
import sys
import argparse
import os
from pathlib import Path
import shutil
import subprocess

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, run_root

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['prepare', 'native', 'engine'])
    parser.add_argument('--cleanup', choices=['original', 'body-first', 'collider-first', 'disable-first'], default='original')
    parser.add_argument('--repeat', type=int, choices=[1, 2], default=1)
    parser.add_argument('--label', default='frozen7')
    parser.add_argument('--creation', action='store_true', help='add Rigidbody before Collider')
    args = parser.parse_args()
    work = Path(os.environ.get('KD_WORK', run_root(ROOT, 'continuation'))) / 'controls'
    work.mkdir(parents=True, exist_ok=True)
    name = ('creation-' if args.creation else '') + args.cleanup
    project = work / name
    if args.mode == 'prepare':
        for folder in ['Assets', 'Packages', 'ProjectSettings']:
            shutil.copytree(ROOT / folder, project / folder, dirs_exist_ok=True)
        source = 'CreationOrder.cs' if args.creation else 'LifecycleOrder.cs'
        shutil.copy2(ROOT / 'ContinuationProbes' / source, project / 'Assets/Scripts/SceneSettingsProbe.cs')
        return
    stem = f'{name}-native{args.repeat}' if args.mode == 'native' else f'{name}-{args.label}'
    output = work / (stem + '.jsonl')
    output.unlink(missing_ok=True)
    env = dict(os.environ, SCENE_SETTINGS_OUT=str(output), SCENE_SETTINGS_NOISE='1', KD_CLEANUP=args.cleanup)
    if args.mode == 'native':
        command = [str(editor_path()),
                   '-batchmode', '-nographics', '-projectPath', str(project),
                   '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work / (stem + '.log'))]
    else:
        command = [os.environ['SCENE_SETTINGS_CLI'], 'run', '-p', str(project), '--headless', '30000', '--no-watch']
    with (work / (stem + '-stdout.log')).open('w') as log:
        result = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '240', *command],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode or not output.exists():
        raise SystemExit(f'{stem}: exit {result.returncode}, output exists: {output.exists()}')
    print(stem, len(output.read_text().splitlines()), 'records', flush=True)


if __name__ == '__main__':
    main()
