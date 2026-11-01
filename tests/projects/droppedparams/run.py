#!/usr/bin/env python3
"""Records the dropped-parameters probe in the Unity Editor or runs it in Unbound.

    run.py create --release R --label L    the Editor makes the probe's assets and scene on a copy,
                                           copied back into Assets/ and ProjectSettings/
    run.py editor --release R --label L    two independent Editor captures (GPU)
    run.py publish --release R --label L   checks they agree byte for byte, writes Reference/
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache; nothing is written here except by `create`. Editors run through the shared
heavy-job launcher, on the GPU (a private Xvfb and VirtualGL).
"""
import argparse
import filecmp
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_file, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'DROPPED_PARAMS_PROBE_OUT'


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def display():
    """A private X server for the Editors, which draw with the GPU through VirtualGL."""
    for number in range(120, 161):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def editor(work):
    name, server = display()
    try:
        jobs = []
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = work/repeat/'capture.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            if out.exists():
                sys.exit(f'refusing to overwrite capture: {out}')
            env = dict(os.environ, DISPLAY=name, **{OUT: str(out)})
            jobs.append(subprocess.Popen(
                [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
                 '-batchmode', '-buildTarget', 'Standalone', '-projectPath', str(project),
                 '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work/repeat/'editor.log')], env=env))
        codes = [job.wait() for job in jobs]
        print('editor exits', codes)
        return int(any(codes))
    finally:
        server.terminate()


def create(work):
    project = copy_project(work/'projects'/'create')
    log = work/'create.log'
    code = subprocess.run([str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode',
                           '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Create',
                           '-logFile', str(log)]).returncode
    print('editor exit', code)
    if code:
        return code
    shutil.copytree(project/'Assets', PROBE/'Assets', dirs_exist_ok=True)
    for name in ('EditorBuildSettings.asset', 'GraphicsSettings.asset'):
        shutil.copyfile(project/'ProjectSettings'/name, PROBE/'ProjectSettings'/name)
    print('copied the Editor-made assets into', PROBE/'Assets')
    return 0


def publish(work):
    a, b = work/'native1'/'capture.jsonl', work/'native2'/'capture.jsonl'
    if not filecmp.cmp(a, b, shallow=False):
        sys.exit('the two Editor captures differ; not publishing')
    for log in (work/'native1'/'editor.log', work/'native2'/'editor.log'):
        if 'NVIDIA' not in next((l for l in open(log, errors='replace') if 'Renderer:' in l), ''):
            sys.exit(f'{log}: the Editor did not render on the NVIDIA GPU; not publishing')
    target = reference_file(PROBE, 'jsonl')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(a, target)
    print('published', target)
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'capture.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, **{OUT: str(out)})
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--target', 'Standalone', '--no-watch', '--frames', '10', '--size', '64x64'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
