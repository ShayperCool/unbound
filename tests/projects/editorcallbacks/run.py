#!/usr/bin/env python3
"""Records the Editor callback and prefab query probe in the Unity Editor or runs it in Unbound.

    run.py create --release R --label L    the Editor makes the prefabs and scenes in a copy
    run.py editor --release R --label L    two independent Editor captures (native1, native2):
                                           interactive Editors (windows on the private X server),
                                           with --batchmode batch-mode ones
    run.py publish --release R --label L   checks that the two captures agree, copies one to Reference/
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every run works on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/editorcallbacks/<label>; nothing is written here.
Editors run through the shared heavy-job launcher, on the GPU (a private Xvfb and VirtualGL),
each under a watchdog.
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
OUT = 'EDITOR_CALLBACKS_OUT'


def copy_project(dest, authored=True):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    if not authored:
        # The Editor makes these: the prefabs, the scenes and the build settings naming them.
        for part in ('Prefabs', 'Scenes'):
            shutil.rmtree(dest/'Assets'/part, ignore_errors=True)
            (dest/'Assets'/(part+'.meta')).unlink(missing_ok=True)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def display():
    """A private X server for the Editors, which draw with the GPU through VirtualGL."""
    for number in range(120, 161):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1920x1080x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def run_editors(work, repeats, method, batchmode=True):
    name, server = display()
    try:
        jobs = []
        for repeat in repeats:
            project = copy_project(work/'projects'/repeat, authored=method != 'ProbeSetup.Build')
            out = work/repeat/'values.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            if out.exists():
                sys.exit(f'refusing to overwrite capture: {out}')
            env = dict(os.environ, DISPLAY=name)
            if method != 'ProbeSetup.Build':
                env[OUT] = str(out)
            command = [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
                       *(['-batchmode'] if batchmode else []), '-projectPath', str(project),
                       '-executeMethod', method, '-logFile', str(work/repeat/'editor.log')]
            if method == 'ProbeSetup.Build':
                command.append('-quit')
            jobs.append(subprocess.Popen(command, env=env))
        codes = [job.wait() for job in jobs]
    finally:
        server.terminate()
    print('editor exits', codes)
    return int(any(codes))


def create(work):
    code = run_editors(work, ['created'], 'ProbeSetup.Build')
    print('created project:', work/'projects'/'created')
    return code


def editor(work, batchmode):
    return run_editors(work, ['native1', 'native2'], 'ProbeSetup.Record', batchmode)


def publish(work):
    a, b = work/'native1'/'values.jsonl', work/'native2'/'values.jsonl'
    if not filecmp.cmp(a, b, shallow=False):
        sys.exit('the two Editor captures differ; not publishing')
    for repeat in ('native1', 'native2'):
        log = (work/repeat/'editor.log').read_text(errors='replace')
        if 'NVIDIA' not in next((line for line in log.splitlines() if 'Renderer:' in line), ''):
            sys.exit(f'{repeat}: NVIDIA GPU renderer was not verified')
    target = reference_file(PROBE, 'jsonl')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(a, target)
    print('published', target)
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'values.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, **{OUT: str(out)})
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--headless', '200'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--batchmode', action='store_true', help='batch-mode Editors (editor mode)')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.batchmode)
    return {'create': create, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
