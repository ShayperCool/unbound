#!/usr/bin/env python3
"""Records the texture import probe in the Unity Editor or runs it in Unbound.

    run.py editor [--label L]    two independent Editor captures per color space
    run.py editor --job-worker-count N [--label L]   one capture per color space with the Editor's
                                 job system at N workers (`-job-worker-count`; not publishable)
    run.py publish [--label L]   checks the two captures agree, copies them to Reference/
    run.py unbound [--label L]   Unbound captures per color space (UNBOUND_CLI: the binary)
    run.py gate [--label L]      Unbound captures compared with Reference/ (every byte exact)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache, with ProjectSettings switched to the profile's color space; nothing is written
here. Profiles: Gamma and Linear on the Standalone target, Android (gamma) on the Android target.
Editors run through the shared heavy-job launcher, on the GPU (a private Xvfb and VirtualGL).
TEXTURE_IMPORT_PROFILES (comma-separated) limits a run to some profiles.

On macOS the Editor runs directly (Metal), `publish` writes only the words in which the macOS
Editor differs from the Linux reference (Reference/<release>/macOS.jsonl, see compare.py) and
`gate` compares with the Linux reference under that overlay.
"""
import argparse
import base64
import filecmp
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
# Profile: (color space, build target).
PROFILES = {'Gamma': (0, 'Standalone'), 'Linear': (1, 'Standalone'), 'Android': (0, 'Android')}
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
MACOS = sys.platform == 'darwin'
# The words in which the macOS Editor's captures differ from the Linux reference.
OVERLAY = 'macOS.jsonl'


def copy_project(dest, profile):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    settings = dest/'ProjectSettings/ProjectSettings.asset'
    text = settings.read_text()
    assert 'm_ActiveColorSpace: 0' in text
    settings.write_text(text.replace('m_ActiveColorSpace: 0', f'm_ActiveColorSpace: {PROFILES[profile][0]}'))
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def profiles():
    wanted = os.environ.get('TEXTURE_IMPORT_PROFILES')
    return [p for p in PROFILES if not wanted or p in wanted.split(',')]


def display():
    """A private X server for the Editors, which draw with the GPU through VirtualGL."""
    for number in range(120, 161):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def macos_editor():
    if 'UNBOUND_PROBE_UNITY' in os.environ:
        return editor_path()
    version = os.environ['UNBOUND_PROBE_UNITY_VERSION']
    return Path(f'/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity')


def editor(work, workers=None):
    name, server = (None, None) if MACOS else display()
    try:
        jobs = []
        repeats = ('native1', 'native2') if workers is None else (f'workers{workers}',)
        extra = [] if workers is None else ['-job-worker-count', str(workers)]
        for space in profiles():
            for repeat in repeats:
                project = copy_project(work/'projects'/f'{space}-{repeat}', space)
                out = work/repeat/f'{space}.jsonl'
                out.parent.mkdir(parents=True, exist_ok=True)
                if out.exists():
                    sys.exit(f'refusing to overwrite capture: {out}')
                env = dict(os.environ, TEXTURE_IMPORT_PROBE_OUT=str(out))
                if MACOS:
                    command = [str(macos_editor())]
                else:
                    env['DISPLAY'] = name
                    command = ['vglrun', '-d', 'egl', str(editor_path())]
                jobs.append(subprocess.Popen(
                    [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', *command,
                     '-batchmode', '-buildTarget', PROFILES[space][1], '-projectPath', str(project),
                     '-executeMethod', 'ProbeRunner.Record', *extra,
                     '-logFile', str(work/repeat/f'{space}.log')], env=env))
        codes = [job.wait() for job in jobs]
        print('editor exits', codes)
        return int(any(codes))
    finally:
        if server:
            server.terminate()


def macos_words(reference, capture):
    """Per key, the 4-byte words in which `capture` differs from `reference`; anything else that
    differs (a key, an info row, a size) refuses."""
    def rows(path):
        return {row['k']: row['v'] for row in map(json.loads, filter(str.strip, open(path, encoding='utf-8')))}
    a, b = rows(reference), rows(capture)
    if a.keys() != b.keys():
        sys.exit(f'{capture}: keys differ from {reference}: {sorted(a.keys() ^ b.keys())}')
    out = {}
    for key in sorted(a):
        if a[key] == b[key]:
            continue
        (size_a, data_a), (size_b, data_b) = a[key].split(':', 1), b[key].split(':', 1)
        if key.endswith('.info') or size_a != size_b:
            sys.exit(f'{capture}: {key} differs beyond its texels; not publishing')
        data_a, data_b = base64.b64decode(data_a), base64.b64decode(data_b)
        out[key] = {str(i // 4): '0x%08x' % int.from_bytes(data_b[i:i + 4], 'little')
                    for i in range(0, len(data_a), 4) if data_a[i:i + 4] != data_b[i:i + 4]}
    return out


def publish(work):
    target = reference_root(PROBE)
    target.mkdir(parents=True, exist_ok=True)
    if MACOS and set(profiles()) != set(PROFILES):
        sys.exit('the macOS overlay is published from every profile at once')
    overlay = []
    for space in profiles():
        a, b = work/'native1'/f'{space}.jsonl', work/'native2'/f'{space}.jsonl'
        if not filecmp.cmp(a, b, shallow=False):
            sys.exit(f'{space}: the two Editor captures differ; not publishing')
        if MACOS:
            overlay += [{'profile': space, 'k': key, 'words': words}
                        for key, words in macos_words(target/f'{space}.jsonl', a).items()]
            continue
        for log in (work/'native1'/f'{space}.log', work/'native2'/f'{space}.log'):
            if 'NVIDIA' not in next((l for l in open(log, errors='replace') if 'Renderer:' in l), ''):
                sys.exit(f'{log}: the Editor did not render on the NVIDIA GPU; not publishing')
        shutil.copyfile(a, target/f'{space}.jsonl')
        print('published', target/f'{space}.jsonl')
    if MACOS:
        with open(target/OVERLAY, 'w', encoding='utf-8') as file:
            file.writelines(json.dumps(row) + '\n' for row in overlay)
        print('published', target/OVERLAY, sum(len(row['words']) for row in overlay), 'words')
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    code = 0
    for space in profiles():
        project = copy_project(work/'projects'/f'{space}-unbound', space)
        out = work/'unbound'/f'{space}.jsonl'
        out.parent.mkdir(parents=True, exist_ok=True)
        if out.exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        env = dict(os.environ, TEXTURE_IMPORT_PROBE_OUT=str(out))
        with (work/'unbound'/f'{space}.log').open('w') as log:
            result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                     '--target', PROFILES[space][1], '--no-watch', '--frames', '10',
                                     '--size', '64x64'],
                                    env=env, stdout=log, stderr=subprocess.STDOUT)
        print(space, 'unbound exit', result.returncode)
        code |= result.returncode
    return code


def gate(work):
    overlay = []
    if MACOS:
        if not (reference_root(PROBE)/OVERLAY).exists():
            sys.exit(f'no macOS Editor values recorded for this release: {reference_root(PROBE)/OVERLAY}')
        overlay = ['--overlay', str(reference_root(PROBE)/OVERLAY)]
    code = unbound(work)
    for space in profiles():
        code |= subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference_root(PROBE)/f'{space}.jsonl'),
                                str(work/'unbound'/f'{space}.jsonl'), *overlay]).returncode
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'gate'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--job-worker-count', type=int, help='editor: one capture with N job workers')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.job_worker_count is not None:
        if args.mode != 'editor':
            parser.error('--job-worker-count is only for editor captures')
        return editor(work, args.job_worker_count)
    return {'editor': editor, 'publish': publish, 'unbound': unbound, 'gate': gate}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
