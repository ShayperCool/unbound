#!/usr/bin/env python3
"""Replay the accepted parent gates into a new private stage directory."""
import os
from pathlib import Path
import shlex
import subprocess

p = Path(os.environ['TRIGGER_WORK'])
p.mkdir(exist_ok=True)
(p / 'work').mkdir(exist_ok=True)
if not (p / 'managed-final').exists():
    (p / 'managed-final').symlink_to(Path(os.environ['UNBOUND_MANAGED_DIR']))
def run(command, **kwargs):
    subprocess.run(command, check=True, **kwargs)

for probe in ['clone-placement', 'transform-clone-placement', 'sleep-inactive',
              'inactive-sequences', 'sleep', 'triggers', 'flags', 'trigger-pairs',
              'ignore-lifecycle', 'kinematic', 'order', 'component', 'barrier']:
    for mode in ['prepare', 'engine']:
        run(['python3', 'tests/projects/physicsscenesettings/parent_probes.py', mode,
             '--probe', probe, '--label', 'final'])
run(['python3', 'tests/projects/physicsscenesettings/regressions.py', 'final'])
for probe in ['timing', 'order']:
    for mode in ['prepare', 'engine']:
        run(['python3', 'tests/projects/trigger2dtiming/run.py', mode,
             '--probe', probe, '--label', 'final'])
heavy = str(Path.home() / '.cache/compat/with-heavy-job')
cli = os.environ['SCENE_SETTINGS_CLI']
for name, var in [('joints2d', 'JOINTS2D_PROBE_OUT'), ('effectors', 'EFFECTORS_PROBE_OUT')]:
    output = p / name
    output.mkdir(exist_ok=True)
    with (p / (name + '.log')).open('w') as log:
        run([heavy, 'timeout', '240', cli, 'run', '-p', 'tests/projects/' + name,
             '--headless', '30000', '--no-watch'],
            env=dict(os.environ, **{var: str(output)}), stdout=log, stderr=subprocess.STDOUT)
for name, var, frames, size in [('graphics', 'GRAPHICS_PROBE_OUT', 30, '960x540'),
                               ('canvaslifecycle', 'CANVAS_LIFECYCLE_OUT', 24, '640x480'),
                               ('lifecycle', 'LIFECYCLE_PROBE_OUT', 12, '960x540')]:
    output = p / (name + '.json' if name == 'graphics' else name + '.jsonl' if name == 'lifecycle' else name)
    with (p / (name + '.log')).open('w') as log:
        run([heavy, 'timeout', '240', cli, 'run', '-p', 'tests/projects/' + name,
             '--no-watch', '--frames', str(frames), '--size', size],
            env=dict(os.environ, **{var: str(output)}), stdout=log, stderr=subprocess.STDOUT)
with (p / 'messages.log').open('w') as log:
    args = shlex.split(subprocess.check_output(['python3', 'tests/projects/messages/compare.py',
                       'args', 'tests/projects/messages/Reference/unity-6000.0.71f1.jsonl'], text=True))
    run([heavy, 'timeout', '240', cli, 'run', '-p', 'tests/projects/messages', '--headless',
         '445', '--size', '960x540', '--no-watch', *args],
        env=dict(os.environ, MESSAGES_PROBE_OUT=str(p / 'messages.jsonl')),
        stdout=log, stderr=subprocess.STDOUT)
run(['python3', 'tests/projects/trigger2dtiming/Evidence/ParentIntegration/replay_matrix.py'])
print('All final engine captures complete', flush=True)
filter = '|'.join('FullyQualifiedName~UnityEngine.Tests.' + name for name in [
    'PhysicsTests', 'Physics2DTests', 'UIElements', 'YogaLayout', 'CanvasOrderTests',
    'CoreTests', 'Physics2DQueryParityTests'])
work = Path(os.environ['QUERY_WORK'])
with (work / 'validation/tests.log').open('w') as log:
    run([heavy, 'dotnet', 'test', 'managed/UnityEngine.Tests/UnityEngine.Tests.csproj',
         '-c', 'Release', '-p:UnboundTarget=' + str(work / 'test-build') + '/',
         '-p:UnboundNativeDir=' + os.environ['UNBOUND_MANAGED_DIR'],
         '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '/nodeReuse:false',
         '--filter', filter, '--logger', 'trx;LogFileName=integrated.trx',
         '--results-directory', str(work / 'validation')], stdout=log, stderr=subprocess.STDOUT)
