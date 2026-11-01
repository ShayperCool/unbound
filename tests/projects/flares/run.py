#!/usr/bin/env python3
"""Records the halo and lens flare probe in the Unity Editor or runs it in Unbound.

    run.py create  --release R --label L   makes the probe's assets and scene in a fresh copy (batchmode)
    run.py editor  --release R --label L   two independent windowed Editor captures (native1, native2)
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)
    run.py check   --release R --label L   validates the two Editor captures without publishing


Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/flares/<label>; nothing is written here except by
publish, which refuses to overwrite an existing reference. Editors run through the shared
heavy-job launcher, windowed on a private Xvfb each, on the GPU through VirtualGL. Publishing
requires each capture to be complete: the Editor exited with 0, the probe's last record is
`complete: true` (and no `fatal`), and its images are exactly those it recorded, at their sizes.
"""
import json
import argparse
import gzip
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import threading
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root

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


def fresh_output(directory):
    if directory.exists():
        sys.exit(f'refusing to overwrite capture: {directory}')
    directory.mkdir(parents=True)
    return directory


def display():
    """A private X server for one Editor."""
    for number in range(120, 200):
        if not Path(f'/tmp/.X{number}-lock').exists() and not Path(f'/tmp/.X11-unix/X{number}').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def stop_compilers(project):
    """Stops only the Unity Roslyn servers and the Hub a windowed Editor started for this
    project copy (the Hub's command line names the project)."""
    for process in Path('/proc').glob('[0-9]*'):
        try:
            command = (process/'cmdline').read_bytes()
            if b'unityhub-bin' in command and str(project).encode()+b'\0' in command:
                # The Hub an Editor starts for its project ignores SIGTERM.
                os.kill(int(process.name), signal.SIGKILL)
                continue
            if b'VBCSCompiler' not in command:
                continue
            env = dict(i.split(b'=', 1) for i in (process/'environ').read_bytes().split(b'\0') if b'=' in i)
            if env.get(b'PWD') == str(project).encode():
                os.kill(int(process.name), signal.SIGTERM)
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            pass


def create(work):
    project = copy_project(work/'projects'/'create')
    log = fresh_output(work/'create')/'editor.log'
    code = subprocess.run([str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode', '-nographics',
                           '-quit', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Create',
                           '-logFile', str(log)], cwd=project, env=dict(os.environ, PWD=str(project))).returncode
    stop_compilers(project)
    print('create exit', code, project/'Assets/Scenes')
    return code


SHUTDOWN_MARKERS = (b'Cleanup mono',)
SHUTDOWN_GRACE = 30
START_GRACE = 1800


def editor_processes(project):
    """The Editor running this project copy (its process group leader) and the Hub it started."""
    found = []
    for process in Path('/proc').glob('[0-9]*'):
        try:
            command = (process/'cmdline').read_bytes().split(b'\0')
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue
        if str(project).encode() not in command:
            continue
        if command[0].endswith(b'/Editor/Unity') or b'unityhub-bin' in command[0]:
            found.append(int(process.name))
    return found


def guard(repeat, project):
    """Ends an Editor that hangs after its shutdown: once its log shows the shutdown sequence and
    it is still alive after a grace period, its process group (Unity, its Package Manager, the Hub
    it started for this project copy) is terminated, and the repeat records the forced exit. A
    forced exit is publishable only with a complete capture identical to the other repeat."""
    log = repeat/'editor.log'
    started = time.monotonic()
    seen = None
    while time.monotonic() - started < START_GRACE:
        processes = editor_processes(project)
        if not processes and (time.monotonic() - started) > 60:
            return
        data = log.read_bytes() if log.exists() else b''
        if seen is None and any(m in data for m in SHUTDOWN_MARKERS):
            seen = time.monotonic()
        if seen is not None and time.monotonic() - seen > SHUTDOWN_GRACE and processes:
            groups = set()
            for pid in processes:
                try:
                    groups.add(os.getpgid(pid))
                except ProcessLookupError:
                    pass
            for sig in (signal.SIGTERM, signal.SIGKILL):
                for group in groups:
                    try:
                        os.killpg(group, sig)
                    except ProcessLookupError:
                        pass
                time.sleep(10)
            (repeat/'forced-exit.txt').write_text(
                f'the Editor logged its shutdown and was still alive {SHUTDOWN_GRACE} s later; '
                f'process groups {sorted(groups)} terminated\n')
            return
        time.sleep(2)


def editor(work):
    started = []
    try:
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = fresh_output(work/repeat)
            name, server = display()
            env = dict(os.environ, DISPLAY=name, FLARES_PROBE_OUT=str(out), PWD=str(project))
            env.pop('XAUTHORITY', None)
            job = subprocess.Popen(
                [str(LAUNCHER), 'timeout', '-s', 'KILL', '1800', 'vglrun', '-d', 'egl', str(editor_path()),
                 '-projectPath', str(project), '-logFile', str(out/'editor.log')], cwd=project, env=env)
            started.append((repeat, project, server, job))
            threading.Thread(target=guard, args=(out, project), daemon=True).start()
        codes = [job.wait() for _, _, _, job in started]
        for (repeat, _, _, _), code in zip(started, codes):
            (work/repeat/'exit.txt').write_text(f'{code}\n')
    finally:
        for _, project, server, job in started:
            if job.poll() is None:
                job.kill()
            server.terminate()
            server.wait()
            stop_compilers(project)
        # An exiting Editor can start its Hub after the first sweep (seen up to a minute later).
        for _ in range(9):
            time.sleep(10)
            for _, project, _, _ in started:
                stop_compilers(project)
    print('editor exits', codes)
    return int(any(codes))


def images(directory):
    return sorted(p.name for p in directory.glob('*.rgba'))


def incomplete(repeat, editor=True):
    """Why a capture cannot be published (None when it is complete)."""
    if editor:
        log = (repeat/'editor.log').read_text(errors='replace') if (repeat/'editor.log').exists() else ''
        if 'NVIDIA' not in next((line for line in log.splitlines() if 'Renderer:' in line), ''):
            return 'NVIDIA GPU renderer was not verified'
        exit_file = repeat/'exit.txt'
        forced = (repeat/'forced-exit.txt').exists()
        if not forced and (not exit_file.exists() or exit_file.read_text().strip() != '0'):
            return 'the Editor did not exit with 0'
    if (repeat/'watchdog.txt').exists() or not (repeat/'values.jsonl').exists():
        return 'the capture did not finish'
    try:
        rows = [json.loads(line) for line in (repeat/'values.jsonl').read_text().splitlines() if line.strip()]
    except json.JSONDecodeError:
        return 'values.jsonl is not complete JSON lines'
    keys = [r.get('k') for r in rows]
    if not rows or rows[-1] != {'k': 'complete', 'v': True} or keys.count('complete') != 1:
        return 'the probe did not record complete: true last'
    if 'fatal' in keys:
        return 'the probe recorded a fatal error'
    if len(set(keys)) != len(keys):
        return 'duplicate keys'
    recorded = {r['k'].removesuffix('.pixels'): r['v'] for r in rows if r['k'].endswith('.pixels')}
    present = {name.removesuffix('.rgba'): (repeat/name).stat().st_size for name in images(repeat)}
    if set(recorded) != set(present):
        return f'images differ from those recorded: {sorted(set(recorded) ^ set(present))[:5]}'
    sizes = [k for k, v in recorded.items() if not isinstance(v, int) or present[k] != 16*v]
    if sizes:
        return f'image sizes differ from their pixel counts: {sizes[:5]}'
    return None


def check(work):
    for repeat in (work/'native1', work/'native2'):
        print(repeat.name, incomplete(repeat) or 'complete')
    return 0


def publish(work):
    a, b = work/'native1', work/'native2'
    for repeat in (a, b):
        reason = incomplete(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    if (a/'values.jsonl').read_bytes() != (b/'values.jsonl').read_bytes():
        sys.exit('the two Editor captures differ in values; not publishing')
    if images(a) != images(b) or any((a/n).read_bytes() != (b/n).read_bytes() for n in images(a)):
        sys.exit('the two Editor captures differ in images; not publishing')
    target = reference_root(PROBE)
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.mkdir(parents=True)
    shutil.copyfile(a/'values.jsonl', target/'values.jsonl')
    for name in images(a):
        with open(target/(name+'.gz'), 'wb') as raw, gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0, compresslevel=9) as z:
            z.write((a/name).read_bytes())
    print('published', target, len(images(a)), 'images')
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = fresh_output(work/'unbound')
    env = dict(os.environ, FLARES_PROBE_OUT=str(out))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '3000', '--size', '128x128'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound', 'check', 'guard'))
    parser.add_argument('--repeat', help='guard: the repeat whose running Editor to watch')
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'guard':
        guard(work/args.repeat, work/'projects'/args.repeat)
        return 0
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound, 'check': check}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
