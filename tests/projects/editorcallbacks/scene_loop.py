#!/usr/bin/env python3
"""Editor callback companion: scene-load progress, activation, roots and render registration."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, publish, run, summary

SOURCE = PROBE / 'SceneLoop'
CACHE = Path(os.environ.get('XDG_CACHE_HOME', Path.home() / '.cache')) / 'unbound/q192'
# The full case ladder finishes near frame 50; isolated profiles finish before frame 16.
MATRIX_FRAMES = 160
PROFILE_FRAMES = 30


def prepare(project, release):
    if project.exists():
        raise SystemExit(f'project exists: {project}')
    shutil.copytree(SOURCE / 'Assets', project / 'Assets')
    for part in ('Packages', 'ProjectSettings'):
        shutil.copytree(REPO / 'tests/projects/lifecycle' / part, project / part)
    settings = SOURCE / 'EditorBuildSettings.asset'
    if settings.exists():
        shutil.copy2(settings, project / 'ProjectSettings/EditorBuildSettings.asset')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text(f'm_EditorVersion: {release}\n')


def complete(directory):
    path = directory / 'values.txt'
    if not path.exists() or path.read_text().splitlines()[-1:] != ['complete']:
        return 'missing completion marker'
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'check'))
    parser.add_argument('--release', required=True)
    profiles = parser.add_mutually_exclusive_group()
    profiles.add_argument('--single-first', action='store_true')
    profiles.add_argument('--force-only', action='store_true')
    profiles.add_argument('--starts-only', action='store_true')
    profiles.add_argument('--queue-only', action='store_true')
    parser.add_argument('--label', default='run-' + str(time.time_ns()))
    parser.add_argument('--repeat', choices=('native1', 'native2'), default='native1')
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = CACHE / args.label
    names = {'single_first': 'scene-loop-single-first', 'force_only': 'scene-loop-force',
             'starts_only': 'scene-loop-starts', 'queue_only': 'scene-loop-queue'}
    name = next((name for flag, name in names.items() if getattr(args, flag)), 'scene-loop')
    reference = PROBE / 'Reference' / ('unity-' + args.release) / (name + '.txt')
    if args.mode == 'publish':
        def write(target, first):
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(first / 'values.txt', target)
        return publish([work / 'native1', work / 'native2'], reference,
                       lambda directory: ['values.txt'], write, complete=[complete], gpu=True)
    repeat = 'created' if args.mode == 'create' else ('unbound' if args.mode == 'check' else args.repeat)
    project = work / 'projects' / repeat
    prepare(project, args.release)
    out = work / repeat
    out.mkdir(parents=True)
    if args.mode in ('create', 'editor'):
        create = args.mode == 'create'
        result = run(EditorJob(project, out / 'editor.log', out,
                     args=['-executeMethod', 'SceneLoopRunner.Create', '-quit'] if create else ['-executeMethod', 'SceneLoopRunner.Record'],
                     batch=create, graphics=True, timeout=900, shutdown_grace=90,
                     env={} if create else {'SCENE_LOAD_LOOP_OUT': str(out / 'values.txt'), 'SCENE_LOAD_SINGLE_FIRST': '1' if args.single_first else '0', 'SCENE_LOAD_FORCE_ONLY': '1' if args.force_only else '0', 'SCENE_LOAD_STARTS_ONLY': '1' if args.starts_only else '0', 'SCENE_LOAD_QUEUE_ONLY': '1' if args.queue_only else '0'},
                     progress_files=[] if create else [out / 'values.txt'],
                     progress_timeout=None if create else 600))
        print(summary(result))
        if result.get('exit_code') != 0 or result.get('forced_exit'):
            return 1
        if create:
            for directory in ('Scenes', 'Prefabs'):
                shutil.copytree(project / 'Assets' / directory, SOURCE / 'Assets' / directory, dirs_exist_ok=True)
                shutil.copy2(project / 'Assets' / (directory + '.meta'), SOURCE / 'Assets' / (directory + '.meta'))
            for path in (project / 'Assets').rglob('*.meta'):
                target = SOURCE / 'Assets' / path.relative_to(project / 'Assets')
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)
            shutil.copy2(project / 'ProjectSettings/EditorBuildSettings.asset', SOURCE / 'EditorBuildSettings.asset')
            return 0
        problem = complete(out)
        if problem:
            raise SystemExit(problem)
        return 0
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
    if not cli:
        parser.error('set UNBOUND_CLI to an immutable runtime')
    frames = MATRIX_FRAMES if name == 'scene-loop' else PROFILE_FRAMES
    env = dict(os.environ, SCENE_LOAD_LOOP_OUT=str(out / 'values.txt'), SCENE_LOAD_SINGLE_FIRST='1' if args.single_first else '0', SCENE_LOAD_FORCE_ONLY='1' if args.force_only else '0', SCENE_LOAD_STARTS_ONLY='1' if args.starts_only else '0', SCENE_LOAD_QUEUE_ONLY='1' if args.queue_only else '0')
    with (out / 'unbound.log').open('w') as log:
        result = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                                 'timeout', '600', cli, 'run', '-p', str(project), '--scene', 'Assets/Scenes/Boot.unity',
                                 '--frames', str(frames), '--size', '320x240', '--no-watch'], env=env,
                                stdout=log, stderr=subprocess.STDOUT)
    if result.returncode or complete(out):
        print(f'engine failed ({result.returncode}): {out / "unbound.log"}')
        return 1
    print('capture:', out / 'values.txt', 'progress reads:', out / 'values.txt.raw.txt')
    from scene_loop_compare import compare
    return compare(reference.read_text().splitlines(), (out / 'values.txt').read_text().splitlines())


if __name__ == '__main__':
    sys.exit(main())
