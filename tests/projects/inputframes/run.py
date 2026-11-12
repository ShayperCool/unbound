#!/usr/bin/env python3
"""Records the input-frames probe in the Unity Editor or runs it in Unbound.

    run.py create --release V --label L  makes the probe's scene with the Editor (batchmode), copies the
                                  scene, the .meta files and the package lock back here
    run.py editor --release V --label L   two independent Editor captures (windowed, real X11 input)
    run.py publish --release V --label L  checks agreement, copies one to Reference/
    run.py unbound --release V --label L  an Unbound capture (UNBOUND_CLI: the binary) and its comparison

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache; nothing but `create` writes here. Editors run through the shared heavy-job
launcher on an X server of their own (Xvfb, no window manager, key auto-repeat off), drawing with
the GPU through VirtualGL.
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_file, run_root, unity_version
from input_capture_validation import capture_problem

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
FRAMES = 740


def copy_project(dest):
    if dest.exists():
        raise FileExistsError(f'Capture project exists; choose a fresh label: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
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
    for repeat in ('native1', 'native2'):
        if (work/repeat).exists() or (work/'projects'/repeat).exists():
            raise FileExistsError(f'Native capture exists; choose a fresh label: {work/repeat}')
    jobs = []
    servers = []
    try:
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = work/repeat/'record.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            name, server = display()
            servers.append(server)
            env = dict(os.environ, DISPLAY=name, INPUTFRAMES_PROBE_DRIVE='1', INPUTFRAMES_PROBE_OUT=str(out))
            # The Editor starts a Hub helper that outlives it: it must not hold our output open.
            with (work/repeat/'stdout.log').open('x') as stdout:
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
    for capture in (a, b):
        problem = capture_problem(capture)
        if problem:
            sys.exit(f'{capture}: {problem}; not publishing')
    result = subprocess.run([sys.executable, str(PROBE/'compare.py'), '--native', str(a), str(b)])
    if result.returncode:
        sys.exit('the two Editor captures disagree; not publishing')
    target = reference_file(PROBE, 'jsonl')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(a, target)
    print('published', target)
    return 0


def unbound(work):
    if (work/'unbound').exists():
        raise FileExistsError(f'Engine capture exists; choose a fresh label: {work}')
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    reference = reference_file(PROBE, 'jsonl')
    problem = capture_problem(reference)
    if problem:
        sys.exit(f'{reference}: {problem}; cannot generate an input replay')
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'record.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    args = subprocess.run([sys.executable, str(PROBE/'compare.py'), 'args', str(reference)],
                          check=True, capture_output=True, text=True).stdout.split()
    env = dict(os.environ, INPUTFRAMES_PROBE_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('x') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project), '--headless',
                                 str(FRAMES), '--size', '960x540', '--no-watch', *args],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode)
    if not out.exists():
        sys.exit(f'no record, see {work/"unbound"/"unbound.log"}')
    with (work/'unbound'/'compare.txt').open('x') as report:
        compared = subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference), str(out)],
                                  stdout=report, stderr=subprocess.STDOUT)
    print((work/'unbound'/'compare.txt').read_text().splitlines()[-1])
    return result.returncode or compared.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]*', args.label):
        parser.error('--label must be a single fresh directory name')
    if not re.fullmatch(r'\d+\.\d+\.\d+[abfp]\d+', args.release):
        parser.error('--release must be an Editor version, such as 6000.0.71f1')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    release_file = work/'release.txt'
    if release_file.exists():
        if release_file.read_text().strip() != args.release:
            parser.error('this label belongs to a different Editor release')
    else:
        release_file.write_text(args.release+'\n')
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
