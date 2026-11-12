#!/usr/bin/env python3
"""Records the texture compression probe in the Unity Editor or runs it in Unbound.

    run.py create --release R        the Editor applies Assets/Editor/TextureCases.txt to the
                                     probe's own importers and writes the scene (after
                                     make_textures.py; writes .meta files here)
    run.py editor --release R --label L    two independent Editor captures per profile
    run.py publish --release R --label L   checks the two captures agree, copies them to Reference/
    run.py unbound --release R --label L   Unbound captures per profile (UNBOUND_CLI: the binary)
    run.py gate --release R --label L      Unbound captures compared with Reference/

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache, with ProjectSettings switched to the profile's color space and Android default
texture compression format. Profiles: Gamma and Linear on the Standalone target; AndroidGamma and
AndroidLinear on the Android target (default compression format unset: ETC), AndroidETC2 (Gamma,
ETC2), AndroidASTC (Gamma, ASTC). Editors run through the shared heavy-job launcher, on the GPU (a private Xvfb and
VirtualGL).
"""
import argparse
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
# Profile: (m_ActiveColorSpace, build target, Android's default TextureCompressionFormat or None).
PROFILES = {
    'Gamma': (0, 'Standalone', None),
    'Linear': (1, 'Standalone', None),
    'AndroidGamma': (0, 'Android', None),
    'AndroidLinear': (1, 'Android', None),
    'AndroidETC2': (0, 'Android', 2),
    'AndroidASTC': (0, 'Android', 3),
}
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def copy_project(dest, profile):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    space, _, android_format = PROFILES[profile]
    settings = dest/'ProjectSettings/ProjectSettings.asset'
    text = settings.read_text()
    assert 'm_ActiveColorSpace: 0' in text
    text = text.replace('m_ActiveColorSpace: 0', f'm_ActiveColorSpace: {space}')
    if android_format is not None:
        empty = '  m_BuildTargetDefaultTextureCompressionFormat: []\n'
        assert empty in text
        text = text.replace(empty, '  m_BuildTargetDefaultTextureCompressionFormat:\n'
                            '  - serializedVersion: 3\n    m_BuildTarget: Android\n'
                            f'    m_Formats: {android_format:02x}000000\n')
    settings.write_text(text)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def display():
    """A private X server for the Editors, which draw with the GPU through VirtualGL."""
    for number in range(120, 161):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def editor_command(project, method, log, extra=(), target='Standalone'):
    return [str(LAUNCHER), '--kind', 'editor-gpu', 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl',
            str(editor_path()), '-batchmode', '-buildTarget', target, '-projectPath', str(project),
            '-executeMethod', method, *extra, '-logFile', str(log)]


def create(work):
    work.mkdir(parents=True, exist_ok=True)
    name, server = display()
    try:
        env = dict(os.environ, DISPLAY=name)
        code = subprocess.run(editor_command(PROBE, 'ProbeRunner.Create', work/'create.log', ['-quit']),
                              env=env).returncode
        print('create exit', code)
        return code
    finally:
        server.terminate()


def editor(work):
    name, server = display()
    try:
        jobs = []
        for profile in PROFILES:
            for repeat in ('native1', 'native2'):
                project = copy_project(work/'projects'/f'{profile}-{repeat}', profile)
                out = work/repeat/f'{profile}.jsonl'
                out.parent.mkdir(parents=True, exist_ok=True)
                if out.exists():
                    sys.exit(f'refusing to overwrite capture: {out}')
                env = dict(os.environ, DISPLAY=name, TEXTURE_COMPRESSION_PROBE_OUT=str(out))
                jobs.append(subprocess.Popen(editor_command(project, 'ProbeRunner.Record', work/repeat/f'{profile}.log',
                                                            target=PROFILES[profile][1]),
                                             env=env))
        codes = [job.wait() for job in jobs]
        print('editor exits', codes)
        return int(any(codes))
    finally:
        server.terminate()


def publish(work):
    target = reference_root(PROBE)
    target.mkdir(parents=True, exist_ok=True)
    for profile in PROFILES:
        a, b = work/'native1'/f'{profile}.jsonl', work/'native2'/f'{profile}.jsonl'
        if not filecmp.cmp(a, b, shallow=False):
            sys.exit(f'{profile}: the two Editor captures differ; not publishing')
        for log in (work/'native1'/f'{profile}.log', work/'native2'/f'{profile}.log'):
            if 'NVIDIA' not in next((l for l in open(log, errors='replace') if 'Renderer:' in l), ''):
                sys.exit(f'{log}: the Editor did not render on the NVIDIA GPU; not publishing')
        previous = target/f'{profile}.jsonl'
        if previous.exists():
            old = {r['k']: r['v'] for r in map(json.loads, previous.read_text().splitlines())}
            new = {r['k']: r['v'] for r in map(json.loads, a.read_text().splitlines())}
            changed = [key for key, value in old.items() if new.get(key) != value]
            if changed:
                sys.exit(f'{profile}: existing reference rows changed: {changed}')
        shutil.copyfile(a, target/f'{profile}.jsonl')
        print('published', target/f'{profile}.jsonl')
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    code = 0
    for profile in PROFILES:
        project = copy_project(work/'projects'/f'{profile}-unbound', profile)
        out = work/'unbound'/f'{profile}.jsonl'
        out.parent.mkdir(parents=True, exist_ok=True)
        if out.exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        env = dict(os.environ, TEXTURE_COMPRESSION_PROBE_OUT=str(out))
        with (work/'unbound'/f'{profile}.log').open('w') as log:
            result = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(project),
                                     '--target', PROFILES[profile][1], '--no-watch', '--frames', '10', '--size', '64x64'],
                                    env=env, stdout=log, stderr=subprocess.STDOUT)
        print(profile, 'unbound exit', result.returncode)
        code |= result.returncode
    return code


def gate(work):
    code = unbound(work)
    for profile in PROFILES:
        subprocess.run([sys.executable, str(PROBE/'block_report.py'), str(reference_root(PROBE)/f'{profile}.jsonl'),
                        str(work/'unbound'/f'{profile}.jsonl')], check=True)
        code |= subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference_root(PROBE)/f'{profile}.jsonl'),
                                str(work/'unbound'/f'{profile}.jsonl'), '--residuals', str(PROBE/'residuals.json'),
                                '--profile', profile]).returncode
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound', 'gate'))
    parser.add_argument('--label', default='create')
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound, 'gate': gate}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
