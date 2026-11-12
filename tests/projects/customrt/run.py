#!/usr/bin/env python3
"""Records the custom render texture probe in the Unity Editor or runs it in Unbound.

    run.py create  --release R --label L   makes the probe scene in a fresh copy (batchmode)
    run.py editor  --release R --label L   two independent windowed Editor captures (native1, native2)
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R
    run.py extend  --release R --label L   adds new cases of two agreeing captures to the existing reference
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)
    run.py check   --release R --label L   validates the two Editor captures without publishing

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/customrt/<label>; nothing is written here except by
publish, which refuses to overwrite an existing reference, and extend, which adds only rows and
images that follow the existing ones when the captures reproduce those byte for byte. Editors run through the shared
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
                os.kill(int(process.name), signal.SIGTERM)
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


def editor(work):
    started = []
    try:
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = fresh_output(work/repeat)
            name, server = display()
            env = dict(os.environ, DISPLAY=name, CUSTOMRT_PROBE_OUT=str(out), PWD=str(project))
            env.pop('XAUTHORITY', None)
            job = subprocess.Popen(
                [str(LAUNCHER), 'timeout', '-s', 'KILL', '1800', 'vglrun', '-d', 'egl', str(editor_path()),
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


def images(directory):
    return sorted(p.name for p in directory.glob('*.rgba'))


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


def agreed(work):
    """The first of two complete, byte-identical Editor captures (exits otherwise)."""
    a, b = work/'native1', work/'native2'
    for repeat in (a, b):
        reason = incomplete(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    if (a/'values.jsonl').read_bytes() != (b/'values.jsonl').read_bytes():
        sys.exit('the two Editor captures differ in values; not publishing')
    if images(a) != images(b) or any((a/n).read_bytes() != (b/n).read_bytes() for n in images(a)):
        sys.exit('the two Editor captures differ in images; not publishing')
    return a


def gz(source, target):
    with open(target, 'wb') as raw, gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0, compresslevel=9) as z:
        z.write(source.read_bytes())


def publish(work):
    a = agreed(work)
    target = reference_root(PROBE)
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.mkdir(parents=True)
    shutil.copyfile(a/'values.jsonl', target/'values.jsonl')
    for name in images(a):
        gz(a/name, target/(name+'.gz'))
    print('published', target, len(images(a)), 'images')
    return 0


def extend(work):
    """Adds the values and images of new cases to the existing reference. The captures must
    reproduce every existing value line in order (the new cases follow them, before the final
    `complete`) and every existing image byte for byte; nothing existing is rewritten."""
    a = agreed(work)
    target = reference_root(PROBE)
    old = (target/'values.jsonl').read_text().splitlines()
    new = (a/'values.jsonl').read_text().splitlines()
    if not old or old[-1] != new[-1] or new[:len(old)-1] != old[:-1]:
        sys.exit('the captures do not reproduce the existing reference values; not extending')
    existing = sorted(p.name.removesuffix('.gz') for p in target.glob('*.rgba.gz'))
    missing = [n for n in existing if n not in images(a)]
    changed = [n for n in existing if n not in missing and gzip.decompress((target/(n+'.gz')).read_bytes()) != (a/n).read_bytes()]
    if missing or changed:
        sys.exit(f'the captures do not reproduce the existing reference images (missing {missing[:5]}, '
                 f'changed {changed[:5]}); not extending')
    added = [n for n in images(a) if n not in existing]
    if len(new) == len(old):
        sys.exit('the captures add no values; nothing to extend')
    for name in added:
        gz(a/name, target/(name+'.gz'))
    shutil.copyfile(a/'values.jsonl', target/'values.jsonl')
    print('extended', target, len(new)-len(old), 'values,', len(added), 'images:', ' '.join(added))
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = fresh_output(work/'unbound')
    env = dict(os.environ, CUSTOMRT_PROBE_OUT=str(out))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '600', '--size', '64x32'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'extend', 'unbound', 'check'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound, 'check': check, 'extend': extend}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
