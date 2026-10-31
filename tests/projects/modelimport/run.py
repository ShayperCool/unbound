#!/usr/bin/env python3
"""Records the model import probe in the Unity Editor or runs it in Unbound.

    run.py editor [--label L]    two independent Editor captures per color space
    run.py publish [--label L]   checks the two captures agree, copies them to Reference/
    run.py unbound [--label L]   Unbound captures per color space (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache, with ProjectSettings switched to the color space; nothing is written here.
Editors run through the shared heavy-job launcher.
"""
import argparse
import filecmp
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
SPACES = {'Gamma': 0, 'Linear': 1}
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def copy_project(dest, space):
    if dest.exists():
        shutil.rmtree(dest)
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    settings = dest/'ProjectSettings/ProjectSettings.asset'
    text = settings.read_text()
    assert 'm_ActiveColorSpace: 0' in text
    settings.write_text(text.replace('m_ActiveColorSpace: 0', f'm_ActiveColorSpace: {SPACES[space]}'))
    return dest


def editor(work):
    jobs = []
    for space in SPACES:
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/f'{space}-{repeat}', space)
            out = work/repeat/f'{space}.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            env = dict(os.environ, MODEL_IMPORT_PROBE_OUT=str(out),
                       MODEL_IMPORT_DESCRIPTIONS_OUT=str(work/repeat/f'{space}.descriptions.txt'))
            log = work/repeat/f'{space}.log'
            jobs.append(subprocess.Popen(
                [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode',
                 '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record',
                 '-logFile', str(log)], env=env))
    codes = [job.wait() for job in jobs]
    print('editor exits', codes)
    return int(any(codes))


def publish(work):
    target = reference_root(PROBE)
    target.mkdir(parents=True, exist_ok=True)
    for space in SPACES:
        a, b = work/'native1'/f'{space}.jsonl', work/'native2'/f'{space}.jsonl'
        if not filecmp.cmp(a, b, shallow=False):
            sys.exit(f'{space}: the two Editor captures differ; not publishing')
        shutil.copyfile(a, target/f'{space}.jsonl')
        print('published', target/f'{space}.jsonl')
    # The descriptions do not depend on the color space.
    a, b = work/'native1'/'Gamma.descriptions.txt', work/'native2'/'Gamma.descriptions.txt'
    if a.read_text() != b.read_text():
        sys.exit('the two Editor description dumps differ; not publishing')
    shutil.copyfile(a, target/'descriptions.txt')
    print('published', target/'descriptions.txt')
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    code = 0
    for space in SPACES:
        project = copy_project(work/'projects'/f'{space}-unbound', space)
        out = work/'unbound'/f'{space}.jsonl'
        out.parent.mkdir(parents=True, exist_ok=True)
        descriptions = work/'unbound'/f'{space}.descriptions.txt'
        descriptions.unlink(missing_ok=True)
        env = dict(os.environ, MODEL_IMPORT_PROBE_OUT=str(out), MODEL_IMPORT_DESCRIPTIONS_OUT=str(descriptions))
        with (work/'unbound'/f'{space}.log').open('w') as log:
            result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                     '--no-watch', '--frames', '10', '--size', '64x64'],
                                    env=env, stdout=log, stderr=subprocess.STDOUT)
        print(space, 'unbound exit', result.returncode)
        code |= result.returncode
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--label', default='current')
    args = parser.parse_args()
    work = run_root(PROBE, args.label)
    return {'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
