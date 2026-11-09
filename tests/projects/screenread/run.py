#!/usr/bin/env python3
"""Records the screen read probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L   two independent windowed Editor captures (native1, native2)
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R.jsonl
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary), compared with
                                           the reference (exit 1 on a difference)
    run.py check   --release R --label L   validates the two Editor captures without publishing

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/screenread/<label>; nothing is written here except by
publish, which refuses to overwrite an existing reference. Editors run through the shared
heavy-job launcher, windowed on a private Xvfb each, on the GPU through VirtualGL. Publishing
requires each capture to be complete: the Editor exited with 0, the probe's last record is
`complete: true` (and no `fatal`). The two repeats must be byte-identical.
"""
import json
import argparse
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
                os.kill(int(process.name), signal.SIGTERM)
                continue
            if b'VBCSCompiler' not in command:
                continue
            env = dict(i.split(b'=', 1) for i in (process/'environ').read_bytes().split(b'\0') if b'=' in i)
            if env.get(b'PWD') == str(project).encode():
                os.kill(int(process.name), signal.SIGTERM)
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            pass


def editor(work):
    started = []
    try:
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = fresh_output(work/repeat)
            name, server = display()
            env = dict(os.environ, DISPLAY=name, SCREENREAD_PROBE_OUT=str(out), PWD=str(project))
            env.pop('XAUTHORITY', None)
            job = subprocess.Popen(
                [str(LAUNCHER), '--kind', 'editor-gpu', 'timeout', '-s', 'KILL', '1800', 'vglrun', '-d', 'egl', str(editor_path()),
                 '-projectPath', str(project), '-logFile', str(out/'editor.log')], cwd=project, env=env)
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
            stop_compilers(project)
    print('editor exits', codes)
    return int(any(codes))


def incomplete(repeat, editor=True):
    """Why a capture cannot be published (None when it is complete)."""
    if editor:
        log = (repeat/'editor.log').read_text(errors='replace') if (repeat/'editor.log').exists() else ''
        if 'NVIDIA' not in next((line for line in log.splitlines() if 'Renderer:' in line), ''):
            return 'NVIDIA GPU renderer was not verified'
        exit_file = repeat/'exit.txt'
        if not exit_file.exists() or exit_file.read_text().strip() != '0':
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
        sys.exit('the two Editor captures differ; not publishing')
    target = reference()
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(a/'values.jsonl', target)
    print('published', target)
    return 0


def reference():
    return reference_file(PROBE, 'jsonl')


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = fresh_output(work/'unbound')
    env = dict(os.environ, SCREENREAD_PROBE_OUT=str(out))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '600', '--size', '64x32'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    if result.returncode:
        return result.returncode
    reason = incomplete(out, editor=False)
    if reason:
        print('unbound capture:', reason)
        return 1
    return compare(reference(), out/'values.jsonl')


def compare(reference_file, capture_file):
    """Every record must equal the reference's; missing and extra keys count."""
    load = lambda p: {r['k']: r['v'] for r in map(json.loads, Path(p).read_text().splitlines()) if r}
    ref, cap = load(reference_file), load(capture_file)
    bad = 0
    for key in sorted(set(ref) | set(cap)):
        if ref.get(key) != cap.get(key):
            bad += 1
            print(f'{key}: reference {json.dumps(ref.get(key))[:200]} != unbound {json.dumps(cap.get(key))[:200]}')
    print(f'{len(set(ref) | set(cap)) - bad}/{len(set(ref) | set(cap))} records match')
    return int(bool(bad))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'check'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'editor': editor, 'publish': publish, 'unbound': unbound, 'check': check}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
