#!/usr/bin/env python3
"""Replay accepted gates into private paths; never write historical acceptance folders."""
import argparse
import importlib.util
import os
from pathlib import Path
import shlex
import subprocess

ROOT = Path(__file__).resolve().parents[3]
WORK = Path(os.environ['LIFECYCLE_WORK'])
HEAVY = str(Path.home()/'.cache/compat/with-heavy-job')
CLI = os.environ['SCENE_SETTINGS_CLI']


def run(args, **kwargs):
    print(shlex.join(map(str, args)), flush=True)
    subprocess.run(list(map(str, args)), check=True, **kwargs)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('group', choices=['focused', 'broad', 'matrix', 'visual', 'tests'])
    a = parser.parse_args()
    if a.group == 'focused':
        for probe in ['clone-placement', 'transform-clone-placement', 'sleep-inactive', 'inactive-sequences',
                      'sleep', 'triggers', 'flags', 'trigger-pairs', 'ignore-lifecycle', 'kinematic', 'order', 'component', 'barrier']:
            for mode in ['prepare', 'engine']:
                run(['python3', ROOT/'tests/projects/physicsscenesettings/parent_probes.py', mode, '--probe', probe, '--label', 'final'])
    if a.group == 'broad':
        run(['python3', ROOT/'tests/projects/trigger2dlifecycle/run.py', 'regressions', '--label', 'final'])
        run(['python3', ROOT/'tests/projects/physicsscenesettings/regressions.py', 'final'])
    if a.group == 'matrix':
        spec = importlib.util.spec_from_file_location('matrix', ROOT/'tests/projects/trigger2dtiming/matrix.py')
        matrix = importlib.util.module_from_spec(spec); spec.loader.exec_module(matrix)
        matrix.ROOT = WORK/'matrix-export'
        (matrix.ROOT/'Evidence').mkdir(parents=True, exist_ok=True)
        # The frozen CLI is ABI5-identical. Pin the exact requested snapshot explicitly.
        original = matrix.subprocess.run
        def pinned(args, **kwargs):
            args = [CLI if str(x).endswith('/parity-baseline-native8/unbound') else x for x in args]
            return original(args, **kwargs)
        matrix.subprocess.run = pinned
        matrix.main()
    if a.group == 'visual':
        for name, var, frames, size in [('graphics', 'GRAPHICS_PROBE_OUT', 30, '960x540'),
                                      ('canvaslifecycle', 'CANVAS_LIFECYCLE_OUT', 24, '640x480'),
                                      ('lifecycle', 'LIFECYCLE_PROBE_OUT', 12, '960x540')]:
            out = WORK/(name+'.json' if name=='graphics' else name+'.jsonl' if name=='lifecycle' else name)
            with (WORK/(name+'.log')).open('w') as log:
                run([HEAVY, 'timeout', '240', CLI, 'run', '-p', ROOT/'tests/projects'/name,
                     '--no-watch', '--frames', str(frames), '--size', size],
                    env=dict(os.environ, **{var: str(out)}), stdout=log, stderr=subprocess.STDOUT)
        args = shlex.split(subprocess.check_output(['python3', str(ROOT/'tests/projects/messages/compare.py'), 'args',
                                                    str(ROOT/'tests/projects/messages/Reference/unity-6000.0.71f1.jsonl')], text=True))
        with (WORK/'messages.log').open('w') as log:
            run([HEAVY, 'timeout', '240', CLI, 'run', '-p', ROOT/'tests/projects/messages', '--headless', '445',
                 '--size', '960x540', '--no-watch', *args], env=dict(os.environ, MESSAGES_PROBE_OUT=str(WORK/'messages.jsonl')),
                stdout=log, stderr=subprocess.STDOUT)
    if a.group == 'tests':
        with (WORK/'tests-integrated.log').open('w') as log:
            run([HEAVY, 'dotnet', 'test', ROOT/'managed/UnityEngine.Tests/UnityEngine.Tests.csproj', '-c', 'Release',
                 '-p:UnboundTarget='+str(WORK/'test-build')+'/', '-p:UnboundNativeDir='+os.environ['UnboundNativeDir'],
                 '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '/nodeReuse:false',
                 '--filter', 'FullyQualifiedName~UnityEngine.Tests.PhysicsTests|FullyQualifiedName~UnityEngine.Tests.Physics2DTests|FullyQualifiedName~UnityEngine.Tests.UIElements|FullyQualifiedName~UnityEngine.Tests.YogaLayout|FullyQualifiedName~UnityEngine.Tests.CanvasOrderTests|FullyQualifiedName~UnityEngine.Tests.CoreTests',
                 '--logger', 'trx;LogFileName=integrated.trx', '--results-directory', WORK/'tests'], stdout=log, stderr=subprocess.STDOUT)


if __name__ == '__main__': main()
