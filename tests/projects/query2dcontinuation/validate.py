#!/usr/bin/env python3
"""Replay current gates into private outputs; --skip-focused resumes completed probes."""
import argparse
import os
from pathlib import Path
import shlex
import shutil
import subprocess

parser=argparse.ArgumentParser()
parser.add_argument('--skip-focused',action='store_true')
args=parser.parse_args()
REPO=Path(__file__).resolve().parents[3]
WORK=Path(os.environ['QUERY_WORK'])
os.environ['SCENE_SETTINGS_CLI']=str(Path(__file__).resolve().with_name('private_cli.py'))
for name in ['SCENE_WORK','SCENE_PARENT_WORK','TRIGGER_WORK']:
    Path(os.environ[name]).mkdir(parents=True,exist_ok=True)
p=Path(os.environ['TRIGGER_WORK'])
(p/'work').mkdir(exist_ok=True)
if not (p/'managed-final').exists():
    (p/'managed-final').symlink_to(Path(os.environ['UNBOUND_MANAGED_DIR']))
def run(command,**kwargs):
    subprocess.run(list(map(str,command)),check=True,**kwargs)
if not args.skip_focused:
    for probe in ['clone-placement','transform-clone-placement','sleep-inactive','inactive-sequences','sleep','triggers','flags','trigger-pairs','ignore-lifecycle','kinematic','order','component','barrier']:
        for mode in ['prepare','engine']:
            run(['python3','tests/projects/physicsscenesettings/parent_probes.py',mode,'--probe',probe,'--label','final'])
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

for probe in ['lifecycle','receivers']:
    for mode in ['prepare','engine']:
        run(['python3',REPO/'tests/projects/trigger2dlifecycle/run.py',mode,'--probe',probe,'--label','final'])
loading=WORK/'loading'
(loading/'logs').mkdir(parents=True)
os.environ['LOADING_WORK']=str(loading)
for folder in ['Assets','Packages','ProjectSettings']:
    shutil.copytree(REPO/'tests/projects/inactive3dloading'/folder,loading/'created'/folder)
for order in ['transform-first','rigidbody-first']:
    for mode in ['prepare','engine']:
        run(['python3',REPO/'tests/projects/inactive3dloading/run.py',mode,'--order',order,'--label','final'])
