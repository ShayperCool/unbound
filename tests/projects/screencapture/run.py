#!/usr/bin/env python3
"""Records the ScreenCapture probe in the Unity Editor or runs it in Unbound.

    run.py create  --release R --label L   makes the probe scene in a fresh copy (batchmode)
    run.py editor  --release R --label L   two independent windowed Editor captures (native1, native2)
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R/<space>
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)
    run.py check   --release R --label L   validates the two Editor captures without publishing

--space Linear (the authored settings) or Gamma (the copies' color space set to Gamma).
--profile internal-state selects only the bounded Linear state and screen callback controls.

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/screencapture/<label>; nothing is written here except by
publish, which refuses to overwrite an existing reference. Editors run through the shared
heavy-job launcher, windowed on a private Xvfb each, on the GPU through VirtualGL. Publishing
requires each capture to be complete: the Editor exited with 0, the probe's last record is
`complete: true` (and no `fatal`), and its images are exactly those it recorded, at their sizes.
"""
import json
import argparse
import gzip
import hashlib
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
SPACE = 'Linear'
PROFILE = 'full'


def source_receipt():
    paths = [p for part in ('Assets', 'Packages', 'ProjectSettings') for p in (PROBE/part).rglob('*') if p.is_file()]
    paths += [PROBE/'run.py', PROBE/'compare.py', PROBE/'internal_state.py',
              PROBE.parent/'rtdepth/compare.py']
    return {str(p.relative_to(REPO)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(paths)}


def record_source(work):
    receipt = {'release': os.environ['UNBOUND_PROBE_UNITY_VERSION'], 'space': SPACE,
               'profile': PROFILE, 'files': source_receipt()}
    path = work/'source.json'
    if path.exists() and json.loads(path.read_text()) != receipt:
        sys.exit('source or capture profile differs from source.json')
    work.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(receipt, indent=2)+'\n')


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    if SPACE == 'Gamma':
        settings = dest/'ProjectSettings/ProjectSettings.asset'
        text = settings.read_text()
        if text.count('m_ActiveColorSpace: 1') != 1:
            sys.exit('expected one m_ActiveColorSpace: 1 in the probe settings')
        settings.write_text(text.replace('m_ActiveColorSpace: 1', 'm_ActiveColorSpace: 0'))
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
    record_source(work)
    codes = []
    for repeat in ('native1', 'native2'):
        project = copy_project(work/'projects'/repeat)
        out = fresh_output(work/repeat)
        name, server = display()
        env = dict(os.environ, DISPLAY=name, SCREENCAPTURE_PROBE_OUT=str(out), PWD=str(project),
                   SCREENCAPTURE_PROBE_PROFILE=PROFILE, UseSharedCompilation='false',
                   MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
        env.pop('XAUTHORITY', None)
        job = None
        try:
            command = [str(LAUNCHER), 'timeout', '-s', 'KILL', '360' if PROFILE == 'internal-state' else '1800',
                       'vglrun', '-d', 'egl', str(editor_path()),
                       '-projectPath', str(project), '-logFile', str(out/'editor.log')]
            (out/'command.json').write_text(json.dumps(command, indent=2)+'\n')
            job = subprocess.Popen(
                command, cwd=project, env=env)
            code = job.wait()
            codes.append(code)
            (out/'exit.txt').write_text(f'{code}\n')
        finally:
            if job is not None and job.poll() is None:
                job.kill()
                job.wait()
            server.terminate()
            server.wait()
            stop_compilers(project)
        reason = incomplete(out)
        if reason:
            print(repeat, reason, file=sys.stderr)
            return 1
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
        return 'the process did not exit with 0'
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
    if PROFILE == 'internal-state':
        from internal_state import coverage
        reason = coverage(rows)
        if reason:
            return reason
    recorded = {r['k'].removesuffix('.pixels'): r['v'] for r in rows if r['k'].endswith('.pixels')}
    present = {name.removesuffix('.rgba'): (repeat/name).stat().st_size for name in images(repeat)}
    if set(recorded) != set(present):
        return f'images differ from those recorded: {sorted(set(recorded) ^ set(present))}'
    sizes = [k for k, v in recorded.items() if not isinstance(v, int) or present[k] != 16*v]
    if sizes:
        return f'image sizes differ from their pixel counts: {sizes}'
    return None


def check(work):
    record_source(work)
    failed = False
    for repeat in (work/'native1', work/'native2'):
        reason = incomplete(repeat)
        print(repeat.name, reason or 'complete')
        failed |= reason is not None
    return int(failed)


def publish(work):
    record_source(work)
    a, b = work/'native1', work/'native2'
    for repeat in (a, b):
        reason = incomplete(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    with (work/'repeat-comparison.log').open('w') as log:
        subprocess.run([sys.executable, str(PROBE/'compare.py'), str(a), str(b),
                        '--report', str(work/'repeat-comparison.json')],
                       stdout=log, stderr=subprocess.STDOUT, check=False)
    receipt = {'source': json.loads((work/'source.json').read_text()), 'captures': {}}
    for repeat in (a, b):
        receipt['captures'][repeat.name] = {
            p.name: hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(repeat.iterdir()) if p.is_file()}
    (work/'artifact-receipt.json').write_text(json.dumps(receipt, indent=2)+'\n')
    if (a/'values.jsonl').read_bytes() != (b/'values.jsonl').read_bytes():
        sys.exit('the two Editor captures differ in values; not publishing')
    if images(a) != images(b) or any((a/n).read_bytes() != (b/n).read_bytes() for n in images(a)):
        sys.exit('the two Editor captures differ in images; not publishing')
    target = reference_root(PROBE)/SPACE if PROFILE == 'full' else reference_root(PROBE)/PROFILE/SPACE
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
    record_source(work)
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = fresh_output(work/'unbound')
    env = dict(os.environ, SCREENCAPTURE_PROBE_OUT=str(out), SCREENCAPTURE_PROBE_PROFILE=PROFILE)
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '600', '--size', '64x32'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    (out/'exit.txt').write_text(f'{result.returncode}\n')
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound', 'check'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--space', choices=('Linear', 'Gamma'), required=True)
    parser.add_argument('--profile', choices=('full', 'internal-state'), default='full')
    args = parser.parse_args()
    global SPACE, PROFILE
    SPACE = args.space
    PROFILE = args.profile
    if PROFILE == 'internal-state' and SPACE != 'Linear':
        parser.error('internal-state is a bounded Linear control')
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound, 'check': check}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
