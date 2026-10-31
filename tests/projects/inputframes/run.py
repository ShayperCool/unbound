#!/usr/bin/env python3
"""Records the input-frames probe in the Unity Editor or runs it in Unbound.

    run.py create                 makes the probe's scene with the Editor (batchmode), copies the
                                  scene, the .meta files and the package lock back here
    run.py editor [--label L]     two independent Editor captures (windowed, real X11 input)
    run.py publish [--label L]    checks the two captures agree (compare.py), copies one to Reference/
    run.py unbound [--label L]    an Unbound capture (UNBOUND_CLI: the binary) and its comparison

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache; nothing but `create` writes here. Editors run through the shared heavy-job
launcher on an X server of their own (Xvfb, no window manager, key auto-repeat off), drawing with
the GPU through VirtualGL.
"""
import argparse
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
FRAMES = 740


def copy_project(dest):
    if dest.exists():
        shutil.rmtree(dest)
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    return dest


def display():
    """A private X server: its display name and process."""
    for number in range(120, 161):
        if Path(f'/tmp/.X{number}-lock').exists() or Path(f'/tmp/.X11-unix/X{number}').exists():
            continue
        server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(50):
            if subprocess.run(['xdpyinfo', '-display', f':{number}'], stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL).returncode == 0:
                break
            subprocess.run(['sleep', '0.1'])
        subprocess.run(['xset', '-display', f':{number}', 'r', 'off'], check=True)
        return f':{number}', server
    sys.exit('no free X display')


def create(work):
    project = copy_project(work/'create')
    log = work/'create.log'
    code = subprocess.run([str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode',
                           '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Create',
                           '-quit', '-logFile', str(log)]).returncode
    if code:
        sys.exit(f'create failed ({code}), see {log}')
    for path in sorted((project/'Assets').rglob('*')):
        relative = path.relative_to(project)
        if path.is_file() and (path.suffix == '.meta' or relative.parts[1] == 'Scenes'):
            target = PROBE/relative
            if not target.exists() or target.read_bytes() != path.read_bytes():
                shutil.copyfile(path, target)
                print('wrote', target.relative_to(REPO))
    for name in ('Packages/packages-lock.json', 'ProjectSettings/EditorBuildSettings.asset'):
        shutil.copyfile(project/name, PROBE/name)
    return 0


def editor(work):
    jobs = []
    servers = []
    try:
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = work/repeat/'record.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            out.unlink(missing_ok=True)
            name, server = display()
            servers.append(server)
            env = dict(os.environ, DISPLAY=name, INPUTFRAMES_PROBE_DRIVE='1', INPUTFRAMES_PROBE_OUT=str(out))
            # The Editor starts a Hub helper that outlives it: it must not hold our output open.
            with (work/repeat/'stdout.log').open('w') as stdout:
                jobs.append((repeat, out, subprocess.Popen(
                    [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
                     '-projectPath', str(project), '-logFile', str(work/repeat/'editor.log')],
                    env=env, stdout=stdout, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)))
        code = 0
        for repeat, out, job in jobs:
            result = job.wait()
            print(repeat, 'editor exit', result, 'record', out if out.exists() else 'missing')
            code |= result or not out.exists()
        return code
    finally:
        for server in servers:
            server.terminate()
        subprocess.run(['pkill', '-f', f'unityhub-bin .*-projectPath {work}/projects/'])


def publish(work):
    a, b = work/'native1'/'record.jsonl', work/'native2'/'record.jsonl'
    result = subprocess.run([sys.executable, str(PROBE/'compare.py'), '--native', str(a), str(b)])
    if result.returncode:
        sys.exit('the two Editor captures disagree; not publishing')
    target = reference_file(PROBE, 'jsonl')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(a, target)
    print('published', target)
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    reference = reference_file(PROBE, 'jsonl')
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'record.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    out.unlink(missing_ok=True)
    args = subprocess.run([sys.executable, str(PROBE/'compare.py'), 'args', str(reference)],
                          check=True, capture_output=True, text=True).stdout.split()
    env = dict(os.environ, INPUTFRAMES_PROBE_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project), '--headless',
                                 str(FRAMES), '--size', '960x540', '--no-watch', *args],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode)
    if not out.exists():
        sys.exit(f'no record, see {work/"unbound"/"unbound.log"}')
    with (work/'unbound'/'compare.txt').open('w') as report:
        compared = subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference), str(out)],
                                  stdout=report, stderr=subprocess.STDOUT)
    print((work/'unbound'/'compare.txt').read_text().splitlines()[-1])
    return result.returncode or compared.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    parser.add_argument('--label', default='current')
    args = parser.parse_args()
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
