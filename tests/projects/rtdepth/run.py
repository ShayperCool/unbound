#!/usr/bin/env python3
"""Records the render texture depth/stencil probe in the Unity Editor or runs it in Unbound.

    run.py create  --release R --label L   makes the probe scene in a fresh copy (batchmode)
    run.py editor  --release R --label L   two independent windowed Editor captures (native1, native2)
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)
    run.py check   --release R --label L   validates the two Editor captures without publishing

--mode controls records the probe's controls (RTDEPTH_PROBE_MODE=controls) instead; their
reference is Reference/unity-R/controls.

--device selects the graphics device of both sides: opengl (the default; the Linux Editor's own
device, on a private Xvfb through VirtualGL; Unbound with --force-glcore) or vulkan (the Editor
with -force-vulkan on the GPU-backed display :0, one capture after the other; Unbound with
--force-vulkan). Vulkan references are Reference/unity-R/vulkan (and vulkan/controls).

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/rtdepth/<label>; nothing is written here except by
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

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
MODE = 'probe'
DEVICE = 'opengl'


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


def xwayland_auth():
    """The authority file of the GPU-backed display :0 (the headless shell's Xwayland)."""
    for process in Path('/proc').glob('[0-9]*'):
        try:
            argv = (process/'cmdline').read_bytes().split(b'\0')
        except (FileNotFoundError, PermissionError):
            continue
        if argv and argv[0].endswith(b'Xwayland') and b':0' in argv and b'-auth' in argv:
            return argv[argv.index(b'-auth')+1].decode()
    sys.exit('no Xwayland on :0 for the Vulkan Editor')


def editor_vulkan(work):
    """The Editor on Vulkan, windowed on :0, one capture after the other."""
    codes = []
    for repeat in ('native1', 'native2'):
        project = copy_project(work/'projects'/repeat)
        out = fresh_output(work/repeat)
        env = dict(os.environ, DISPLAY=':0', XAUTHORITY=xwayland_auth(), RTDEPTH_PROBE_OUT=str(out),
                   PWD=str(project), RTDEPTH_PROBE_MODE=MODE)
        job = subprocess.Popen(
            [str(LAUNCHER), '--kind', 'editor-gpu', 'timeout', '-s', 'KILL', '1800', str(editor_path()),
             '-force-vulkan', '-projectPath', str(project), '-logFile', str(out/'editor.log')], cwd=project, env=env)
        try:
            codes.append(job.wait())
        finally:
            if job.poll() is None:
                job.kill()
            stop_compilers(project)
        (out/'exit.txt').write_text(f'{codes[-1]}\n')
    print('editor exits', codes)
    return int(any(codes))


def editor(work):
    if DEVICE == 'vulkan':
        return editor_vulkan(work)
    started = []
    try:
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = fresh_output(work/repeat)
            name, server = display()
            env = dict(os.environ, DISPLAY=name, RTDEPTH_PROBE_OUT=str(out), PWD=str(project), RTDEPTH_PROBE_MODE=MODE)
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
        device = '[Vulkan init] Physical Device' if DEVICE == 'vulkan' else 'Renderer:'
        if 'NVIDIA' not in next((line for line in log.splitlines() if device in line), ''):
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
    target = reference_root(PROBE)/'vulkan' if DEVICE == 'vulkan' else reference_root(PROBE)
    target = target/'controls' if MODE == 'controls' else target
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
    env = dict(os.environ, RTDEPTH_PROBE_OUT=str(out), RTDEPTH_PROBE_MODE=MODE)
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '600', '--size', '64x32',
                                 '--force-vulkan' if DEVICE == 'vulkan' else '--force-glcore'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound', 'check'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--mode', dest='probe_mode', choices=('probe', 'controls'), default='probe')
    parser.add_argument('--device', choices=('opengl', 'vulkan'), default='opengl')
    args = parser.parse_args()
    global MODE, DEVICE
    MODE = args.probe_mode
    DEVICE = args.device
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound, 'check': check}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
