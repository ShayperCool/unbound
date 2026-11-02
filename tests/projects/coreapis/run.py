#!/usr/bin/env python3
"""Records the core API probe in the Unity Editor or runs it in Unbound.

    run.py create --release R --label L    runs ProbeRunner.Create on a copy, copies its assets back here
    run.py editor --release R --label L    two independent Editor captures (native1, native2) in
                                           windowed Editors (the Game view renders)
    run.py publish --release R --label L   checks that both agree, copies one to Reference/
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/coreapis/<label>; nothing is written here.
Editors run through the shared heavy-job launcher, on the GPU (a private Xvfb and VirtualGL).
"""
import argparse
import filecmp
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_file, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def display():
    """A private X server for one Editor, which draws with the GPU through VirtualGL."""
    for number in range(120, 200):
        if not Path(f'/tmp/.X{number}-lock').exists() and not Path(f'/tmp/.X11-unix/X{number}').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def stop_helpers(project):
    """Stops only the Hub and the Unity Roslyn servers a windowed Editor started for this project
    copy (the Hub's command line names the project)."""
    for process in Path('/proc').glob('[0-9]*'):
        try:
            command = (process/'cmdline').read_bytes()
            if b'unityhub-bin' in command and str(project).encode()+b'\0' in command:
                os.kill(int(process.name), signal.SIGTERM)
                continue
            if b'VBCSCompiler' not in command:
                continue
            env = dict(i.split(b'=', 1) for i in (process/'environ').read_bytes().split(b'\0') if b'=' in i)
            if env.get(b'PWD') == str(project).encode():
                os.kill(int(process.name), signal.SIGTERM)
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            pass


def editor(work, windowed=True, repeats=('native1', 'native2')):
    """Windowed Editors start Play mode from ProbeRunner's InitializeOnLoad; batch mode (where the
    Game view does not render) through -executeMethod ProbeRunner.Record."""
    started = []
    codes = []
    try:
        for repeat in repeats:
            project = copy_project(work/'projects'/repeat)
            out = work/repeat/'values.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            if out.exists():
                sys.exit(f'refusing to overwrite capture: {out}')
            name, server = display()
            env = dict(os.environ, DISPLAY=name, CORE_APIS_OUT=str(out), PWD=str(project))
            env.pop('XAUTHORITY', None)
            mode = [] if windowed else ['-batchmode', '-executeMethod', 'ProbeRunner.Record']
            job = subprocess.Popen(
                [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
                 *mode, '-projectPath', str(project), '-logFile', str(work/repeat/'editor.log')], cwd=project, env=env)
            started.append((repeat, project, server, job))
        codes = [job.wait() for _, _, _, job in started]
        for (repeat, _, _, _), code in zip(started, codes):
            (work/repeat/'exit.txt').write_text(f'{code}\n')
    finally:
        for _, project, server, job in started:
            if job.poll() is None:
                job.kill()
            server.terminate()
            server.wait()
            stop_helpers(project)
    print('editor exits', codes)
    return int(any(codes) or not codes)


def create(work):
    """Runs ProbeRunner.Create in a batch-mode Editor on a copy and copies what it made (the scene,
    prefabs, assets and every .meta) back into this probe."""
    project = copy_project(work/'projects'/'create')
    result = subprocess.run([str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode', '-nographics',
                             '-quit', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Create',
                             '-logFile', str(work/'create.log')])
    if result.returncode:
        sys.exit(f'create failed: {result.returncode}')
    for path in sorted((project/'Assets').rglob('*')):
        relative = path.relative_to(project)
        if path.is_file() and not (PROBE/relative).exists():
            (PROBE/relative).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, PROBE/relative)
            print('created', relative)
    shutil.copyfile(project/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
    return 0


def publish(work):
    a, b = work/'native1'/'values.jsonl', work/'native2'/'values.jsonl'
    if not filecmp.cmp(a, b, shallow=False):
        sys.exit('the two Editor captures differ; not publishing')
    for repeat in ('native1', 'native2'):
        if (work/repeat/'exit.txt').read_text().strip() != '0' or (work/repeat/'values.jsonl.watchdog').exists():
            sys.exit(f'{repeat}: the Editor did not finish Play mode and exit with 0')
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
    env = dict(os.environ, CORE_APIS_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '200', '--size', '320x240'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'batch', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    modes = {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound,
             'batch': lambda w: editor(w, windowed=False, repeats=('batch',))}
    return modes[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
