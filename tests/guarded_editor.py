"""The windowed GPU Editor of the guarded probe runners (activationorder, destroycontract,
shaderpasscount, shaderkeywords): the graphics device each OS records on, its launch flags and the
strict checks of the Editor's result, command and log, defined once for those runners' guards.

On Linux the Editor is the OpenGL Core Editor on the NVIDIA GPU through VirtualGL (`vglrun -d egl`,
`-force-glcore`, through the heavy-job launcher). On macOS arm64 it is the windowed macOS Editor
started directly (tests/editor_runner.py `run_macos`), on its one device, Metal; its references
are the probes' Metal ones (`probe_paths.device_reference`).
"""
import argparse
import os
from pathlib import Path
import re
import shutil

from editor_runner import EditorJob, run, summary, vulkan_display_env
from probe_paths import MACOS_ARM64, run_root

# UnityEngine.SystemInfo.graphicsDeviceType of the capture's Editor.
DEVICE = 'Metal' if MACOS_ARM64 else 'OpenGLCore'
# Editor arguments that select that device.
DEVICE_ARGS = [] if MACOS_ARM64 else ['-force-glcore']
# The Editor log line naming the device it draws with.
RENDERER_LINE = r'^Initializing Metal device caps: ' if MACOS_ARM64 else r'^Renderer:'


def result_problems(result):
    """The device part of a strict Editor result (editor-run.json): the GPU the reference needs,
    a GPU Editor job and, on Linux, the launcher's exit 0."""
    problems = []
    renderer = result.get('renderer') or ''
    if MACOS_ARM64:
        if result.get('device') != 'metal' or not renderer.startswith('Metal: ') or result.get('renderer_ok') is not True:
            problems.append(f"renderer {result.get('renderer')!r} is not the Metal device")
    elif 'NVIDIA' not in renderer or result.get('renderer_ok') is not True:
        problems.append(f"renderer {result.get('renderer')!r} is not the NVIDIA GPU")
    if result.get('kind') != 'editor-gpu' or result.get('require_gpu') is not True:
        problems.append(f"kind {result.get('kind')} / require_gpu {result.get('require_gpu')}")
    if not MACOS_ARM64:
        launcher = result.get('launcher') or {}
        if launcher.get('exit') != 0:
            problems.append(f"launcher exit {launcher.get('exit')}")
    return problems


def command_problems(command, release, method='ProbeRunner.Record'):
    """The planned windowed GPU command: the release's Editor (through `vglrun -d egl` on Linux),
    the runner's one -executeMethod, no -batchmode, -nographics or -quit."""
    problems = []
    if MACOS_ARM64:
        editor = command[0] if command else ''
        if not editor.endswith('/Unity.app/Contents/MacOS/Unity') or f'/{release}/' not in editor:
            problems.append(f'Editor {editor!r} is not release {release}')
    else:
        if command[:3] != ['vglrun', '-d', 'egl']:
            problems.append(f'not a vglrun EGL command: {command[:3]}')
        editor = command[3] if len(command) > 3 else ''
        if not editor.endswith('/Editor/Unity') or f'/{release}/' not in editor:
            problems.append(f'Editor {editor!r} is not release {release}')
    if '-batchmode' in command:
        problems.append('-batchmode must not be passed')
    for flag in ('-nographics', '-quit'):
        if flag in command:
            problems.append(f'{flag} must not be passed')
    if ('-executeMethod', method) not in list(zip(command, command[1:])):
        problems.append(f'-executeMethod {method} is missing')
    if command.count('-executeMethod') != 1:
        problems.append('more than one -executeMethod')
    return problems


def renderer_logged(lines):
    """Whether the Editor log names the device it draws with."""
    return any(re.search(RENDERER_LINE, line) for line in lines)


def display_env():
    """The display the Linux Editor draws on (tests/editor_runner.py `vulkan_display_env`, the
    GPU-backed :0); the macOS Editor needs none."""
    if MACOS_ARM64:
        return {}
    return vulkan_display_env()


def editor_data(release):
    """The Editor's data directory of `release`, which holds its bundled .NET runtime
    (`NetCoreRuntime`) and compiler (`DotNetSdkRoslyn`)."""
    if MACOS_ARM64:
        return Path('/Applications/Unity/Hub/Editor')/release/'Unity.app/Contents'
    return Path.home()/'Unity/Hub/Editor'/release/'Editor/Data'


def project_of(command):
    """The -projectPath of an Editor command ('' without one)."""
    return command[command.index('-projectPath')+1] if '-projectPath' in command[:-1] else ''


def batch_fixture_problems(result, copy):
    """Why an Editor result is not a strict exit-0 batchmode GPU Editor run of the project copy
    `copy` on this OS's device (the preflights' compile fixtures)."""
    command = result.get('command') or []
    problems = []
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        problems.append(f"Editor exit {result.get('exit_code')}, forced {result.get('forced_exit')}")
    renderer = result.get('renderer') or ''
    if result.get('kind') != 'editor-gpu' or not (renderer.startswith('Metal: ') if MACOS_ARM64 else 'NVIDIA' in renderer):
        problems.append(f"kind {result.get('kind')}, renderer {renderer!r} is not this OS's GPU Editor")
    if '-batchmode' not in command:
        problems.append('not a batchmode command')
    if not project_of(command) or Path(project_of(command)).resolve() != Path(copy).resolve():
        problems.append(f'the command opens {project_of(command)!r}, not this copy')
    return problems


def fixture(probe, release, label):
    """A compile fixture for a guarded probe's preflight.py: one batchmode GPU Editor run (-quit)
    on a fresh copy of the probe's Assets/Packages/ProjectSettings under
    validation/runs/<probe>/<label>, its launch receipt binding the copy's Assets, Packages and
    ProjectSettings. Returns (launch directory, the copy's Bee graph directory)."""
    probe = Path(probe).resolve()
    work = run_root(probe, label)
    project, out = work/'project', work/'launch'
    if work.exists():
        raise SystemExit(f'refusing to reuse {work}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(probe/part, project/part)
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = release
    job = EditorJob(project, out/'editor.log', out, args=[*DEVICE_ARGS, '-quit'], batch=True, graphics=True,
                    env={**display_env(), 'UNBOUND_PROBE_UNITY_VERSION': release}, timeout=1200,
                    label=probe.name+'-fixture',
                    launch_inputs={'release': release, 'paths': {'assets': str(project/'Assets'),
                                                                 'packages': str(project/'Packages'),
                                                                 'settings': str(project/'ProjectSettings')}})
    result = run(job)
    print(summary(result))
    dags = sorted(d for d in (project/'Library/Bee/artifacts').glob('*.dag') if (d/'Assembly-CSharp.rsp').is_file())
    problems = batch_fixture_problems(result, project)+([] if len(dags) == 1 else [f'{len(dags)} compile graphs'])
    if problems:
        raise SystemExit('fixture: '+'; '.join(problems))
    return out, dags[0]


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='guarded_editor.py fixture --probe P --release R --label L: '
                                     'a preflight compile fixture (see fixture()).')
    parser.add_argument('mode', choices=('fixture',))
    parser.add_argument('--probe', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    launch, dag = fixture(args.probe, args.release, args.label)
    print(f'--launch {launch} --rsp-dir {dag}')
