#!/usr/bin/env python3
"""Replay integration captures into an external work directory with explicit runtime paths."""
import argparse
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, run_root

FOCUSED = ['clone-placement', 'transform-clone-placement', 'sleep-inactive', 'inactive-sequences',
           'sleep', 'triggers', 'flags', 'trigger-pairs', 'ignore-lifecycle', 'kinematic',
           'order', 'component', 'barrier']


def main(probe=ROOT, lifecycle=True, motion=False):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', type=Path, default=Path(os.environ.get('QUERY_WORK', run_root(probe))))
    parser.add_argument('--cli', default=os.environ.get('UNBOUND_CLI', os.environ.get('SCENE_SETTINGS_CLI')),
                        help='Engine executable; defaults to the private-copy wrapper using --work/unbound')
    parser.add_argument('--managed', type=Path, default=os.environ.get('UNBOUND_MANAGED_DIR'), required=False)
    parser.add_argument('--matrix-baseline', type=Path, default=os.environ.get('MATRIX_ACCEPTED'),
                        help='Optional prior matrix capture directory for byte-identical regression checks')
    parser.add_argument('--skip-focused', action='store_true', help='Resume after existing focused captures')
    parser.add_argument('--skip-tests', action='store_true', help='Resume with an already completed test run')
    parser.add_argument('--resume-lifecycle', action='store_true', help='Run only contact lifecycle and loading captures')
    args = parser.parse_args()
    if args.managed is None:
        parser.error('--managed or UNBOUND_MANAGED_DIR must select the runtime being measured')
    if not args.managed.is_dir():
        parser.error('managed runtime directory does not exist: '+str(args.managed))
    if args.resume_lifecycle and not lifecycle:
        parser.error('this stage does not include contact lifecycle/loading captures')
    work = args.work.resolve()
    if work.is_relative_to(REPO):
        parser.error('--work must be outside the repository')
    cli = args.cli or str(ROOT/'private_cli.py')
    scene_work = Path(os.environ.get('SCENE_WORK', work/'scene')).resolve()
    focused_work = Path(os.environ.get('SCENE_PARENT_WORK', work/'scene-parent')).resolve()
    trigger_work = Path(os.environ.get('TRIGGER_WORK', work/'trigger')).resolve()
    loading = Path(os.environ.get('LOADING_WORK', work/'loading')).resolve()
    if any(p.is_relative_to(REPO) for p in [scene_work, focused_work, trigger_work, loading]):
        parser.error('capture paths must be outside the repository')
    env = dict(os.environ, QUERY_WORK=str(work), SCENE_WORK=str(scene_work),
               SCENE_PARENT_WORK=str(focused_work), TRIGGER_WORK=str(trigger_work), LOADING_WORK=str(loading),
               SCENE_SETTINGS_CLI=cli, UNBOUND_CLI=cli, UNBOUND_MANAGED_DIR=str(args.managed.resolve()),
               LD_LIBRARY_PATH=str(args.managed.resolve()))
    for path in [work, scene_work, focused_work, trigger_work, trigger_work/'work', work/'validation']:
        path.mkdir(parents=True, exist_ok=True)
    if not (trigger_work/'managed-final').exists():
        (trigger_work/'managed-final').symlink_to(args.managed.resolve())
    heavy = str(Path.home()/'.cache/compat/with-heavy-job')

    def run(command, **kwargs):
        subprocess.run(list(map(str, command)), check=True, cwd=REPO, env=kwargs.pop('env', env), **kwargs)

    def finish_lifecycle():
        for name in ['lifecycle', 'receivers']:
            for mode in ['prepare', 'engine']:
                run([sys.executable, REPO/'tests/projects/trigger2dlifecycle/run.py', mode,
                     '--probe', name, '--label', 'final'])
        (loading/'logs').mkdir(parents=True, exist_ok=True)
        for folder in ['Assets', 'Packages', 'ProjectSettings']:
            shutil.copytree(REPO/'tests/projects/inactive3dloading'/folder, loading/'created'/folder, dirs_exist_ok=True)
        for order in ['transform-first', 'rigidbody-first']:
            for mode in ['prepare', 'engine']:
                run([sys.executable, REPO/'tests/projects/inactive3dloading/run.py', mode,
                     '--order', order, '--label', 'final'])

    if args.resume_lifecycle:
        finish_lifecycle()
        return 0
    if not args.skip_focused:
        for name in FOCUSED:
            for mode in ['prepare', 'engine']:
                run([sys.executable, REPO/'tests/projects/physicsscenesettings/parent_probes.py', mode,
                     '--probe', name, '--label', 'final'])
    run([sys.executable, REPO/'tests/projects/physicsscenesettings/regressions.py', 'final'])
    for name in ['timing', 'order']:
        for mode in ['prepare', 'engine']:
            run([sys.executable, REPO/'tests/projects/trigger2dtiming/run.py', mode,
                 '--probe', name, '--label', 'final'])
    for name, variable in [('joints2d', 'JOINTS2D_PROBE_OUT'), ('effectors', 'EFFECTORS_PROBE_OUT')]:
        output = trigger_work/name
        output.mkdir(exist_ok=True)
        with (trigger_work/(name+'.log')).open('w') as log:
            run([heavy, 'timeout', '240', cli, 'run', '-p', REPO/'tests/projects'/name,
                 '--headless', '30000', '--no-watch'], env=dict(env, **{variable:str(output)}),
                stdout=log, stderr=subprocess.STDOUT)
    for name, variable, frames, size in [('graphics', 'GRAPHICS_PROBE_OUT', 30, '960x540'),
                                         ('canvaslifecycle', 'CANVAS_LIFECYCLE_OUT', 24, '640x480'),
                                         ('lifecycle', 'LIFECYCLE_PROBE_OUT', 12, '960x540')]:
        output = trigger_work/(name+'.json' if name == 'graphics' else name+'.jsonl' if name == 'lifecycle' else name)
        with (trigger_work/(name+'.log')).open('w') as log:
            run([heavy, 'timeout', '240', cli, 'run', '-p', REPO/'tests/projects'/name,
                 '--no-watch', '--frames', str(frames), '--size', size], env=dict(env, **{variable:str(output)}),
                stdout=log, stderr=subprocess.STDOUT)
    message_args = shlex.split(subprocess.check_output(
        [sys.executable, str(REPO/'tests/projects/messages/compare.py'), 'args',
         str(reference_file(REPO/'tests/projects/messages', 'jsonl'))], text=True))
    with (trigger_work/'messages.log').open('w') as log:
        run([heavy, 'timeout', '240', cli, 'run', '-p', REPO/'tests/projects/messages', '--headless',
             '445', '--size', '960x540', '--no-watch', *message_args],
            env=dict(env, MESSAGES_PROBE_OUT=str(trigger_work/'messages.jsonl')), stdout=log, stderr=subprocess.STDOUT)
    matrix = [sys.executable, REPO/'tests/projects/trigger2dtiming/replay_matrix.py']
    if args.matrix_baseline: matrix += ['--accepted', args.matrix_baseline]
    run(matrix)
    if not args.skip_tests:
        classes = ['PhysicsTests', 'Physics2DTests', 'UIElements', 'YogaLayout', 'CanvasOrderTests',
                   'CoreTests', 'Physics2DQueryParityTests']+(['Physics2DMotionParityTests'] if motion else [])
        filter = '|'.join('FullyQualifiedName~UnityEngine.Tests.'+name for name in classes)
        results = work/('tests' if motion else 'validation')
        results.mkdir(exist_ok=True)
        with (work/'validation/tests.log').open('w') as log:
            run([heavy, 'dotnet', 'test', REPO/'managed/UnityEngine.Tests/UnityEngine.Tests.csproj', '-c', 'Release',
                 '-p:UnboundTarget='+str(work/'test-build')+'/', '-p:UnboundNativeDir='+str(args.managed.resolve()),
                 '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '/nodeReuse:false', '--filter', filter,
                 '--logger', 'trx;LogFileName='+('combined.trx' if motion else 'integrated.trx'),
                 '--results-directory', results], stdout=log, stderr=subprocess.STDOUT)
    if lifecycle: finish_lifecycle()
    print('Requested captures complete:', work, flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
