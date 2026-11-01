#!/usr/bin/env python3
"""Records the Texture2D pixel read probe in the Unity Editor or runs it in Unbound.

    run.py editor --release R --label L    two independent Editor captures per profile
    run.py publish --release R --label L   checks the two captures agree, copies one to Reference/
    run.py unbound --release R --label L   one Unbound capture per profile (UNBOUND_CLI: the binary)

The probe reuses the texture array fixture (tests/projects/texturearrays): every capture runs on
a fresh copy of that fixture in the validation cache, with its probe script replaced by
Assets/Scripts/TexturePixelsProbe.cs (same class and GUID) and ProjectSettings switched to the
profile's color space. Profiles: Gamma and Linear on the Standalone target. Editors run through
the shared heavy-job launcher on the GPU (a private Xvfb and VirtualGL); do not wrap this driver.
"""
import argparse
import filecmp
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
PROFILES = ('Gamma', 'Linear')
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
_spec = importlib.util.spec_from_file_location('texturearrays_run', PROBE.parent/'texturearrays/run.py')
fixture = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fixture)


def copy_project(dest, profile):
    fixture.copy_project(dest, profile)
    shutil.copyfile(PROBE/'Assets/Scripts/TexturePixelsProbe.cs', dest/'Assets/Scripts/TextureArrayProbe.cs')
    return dest


def editor(work):
    name, server = fixture.display()
    try:
        jobs = []
        for space in PROFILES:
            for repeat in ('native1', 'native2'):
                project = copy_project(work/'projects'/f'{space}-{repeat}', space)
                out = work/repeat/f'{space}.jsonl'
                out.parent.mkdir(parents=True, exist_ok=True)
                if out.exists():
                    sys.exit(f'refusing to overwrite capture: {out}')
                env = dict(os.environ, DISPLAY=name, TEXTURE_PIXELS_PROBE_OUT=str(out))
                jobs.append(subprocess.Popen(
                    [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
                     '-batchmode', '-buildTarget', 'Standalone', '-projectPath', str(project),
                     '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work/repeat/f'{space}.log')], env=env))
        codes = [job.wait() for job in jobs]
        print('editor exits', codes)
        return int(any(codes))
    finally:
        server.terminate()


def publish(work):
    target = reference_root(PROBE)
    target.mkdir(parents=True, exist_ok=True)
    for space in PROFILES:
        a, b = work/'native1'/f'{space}.jsonl', work/'native2'/f'{space}.jsonl'
        if not filecmp.cmp(a, b, shallow=False):
            sys.exit(f'{space}: the two Editor captures differ; not publishing')
        for log in (work/'native1'/f'{space}.log', work/'native2'/f'{space}.log'):
            if 'NVIDIA' not in next((l for l in open(log, errors='replace') if 'Renderer:' in l), ''):
                sys.exit(f'{log}: the Editor did not render on the NVIDIA GPU; not publishing')
        shutil.copyfile(a, target/f'{space}.jsonl')
        print('published', target/f'{space}.jsonl')
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    code = 0
    for space in PROFILES:
        project = copy_project(work/'projects'/f'{space}-unbound', space)
        out = work/'unbound'/f'{space}.jsonl'
        out.parent.mkdir(parents=True, exist_ok=True)
        if out.exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        env = dict(os.environ, TEXTURE_PIXELS_PROBE_OUT=str(out))
        with (work/'unbound'/f'{space}.log').open('w') as log:
            result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                     '--target', 'Standalone', '--no-watch', '--frames', '10', '--size', '64x64'],
                                    env=env, stdout=log, stderr=subprocess.STDOUT)
        print(space, 'unbound exit', result.returncode)
        code |= result.returncode
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
