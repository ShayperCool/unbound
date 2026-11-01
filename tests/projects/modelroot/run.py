#!/usr/bin/env python3
"""Records the model root motion probe in the Unity Editor or runs it in Unbound.

    run.py create --release R --label L    makes the models' import settings and controllers
                                           in a copy and copies them back into Assets/
    run.py create-review --release R --label L   creates the MatchTarget review controller
    run.py editor --release R --label L    two independent Editor captures (native1, native2)
    run.py publish --release R --label L   checks that the two captures agree, copies one to Reference/
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/modelroot/<label>; only `create` writes here.
Editors run through the shared heavy-job launcher, on the GPU (a private Xvfb and VirtualGL).
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
    # Reuse the humanoid probe's authored rig; no repeated model input in Git.
    rig = PROBE.parent/'humanoid/Assets/Resources/HumanA.fbx'
    for suffix in ('', '.meta'):
        shutil.copyfile(str(rig)+suffix, dest/('Assets/Resources/ReviewHumanA.fbx'+suffix))
    return dest


def display():
    """A private X server for the Editors, which draw with the GPU through VirtualGL."""
    for number in range(120, 161):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def editor(work):
    name, server = display()
    try:
        jobs = []
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = work/repeat/'values.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            if out.exists():
                sys.exit(f'refusing to overwrite capture: {out}')
            env = dict(os.environ, DISPLAY=name, MODEL_ROOT_OUT=str(out))
            jobs.append(subprocess.Popen(
                [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
                 '-batchmode', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record',
                 '-logFile', str(work/repeat/'editor.log')], env=env))
        codes = [job.wait() for job in jobs]
        for repeat, code in zip(('native1', 'native2'), codes):
            (work/repeat/'exit-code.txt').write_text(str(code)+'\n')
    finally:
        server.terminate()
    print('editor exits', codes)
    return int(any(codes))


def create(work):
    """Runs ProbeRunner.Create in a copy (nographics) and copies its new assets and metas back."""
    project = copy_project(work/'projects'/'create')
    (work/'create').mkdir(parents=True, exist_ok=True)
    code = subprocess.run([str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode',
                           '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Create',
                           '-logFile', str(work/'create'/'editor.log')]).returncode
    if code:
        sys.exit(f'ProbeRunner.Create failed ({code}), see {work/"create"/"editor.log"}')
    copied = []
    for path in sorted((project/'Assets').rglob('*')):
        if path.name in ('ReviewHumanA.fbx', 'ReviewHumanA.fbx.meta'):
            continue
        target = PROBE/path.relative_to(project)
        if path.is_file() and (not target.exists() or not filecmp.cmp(path, target, shallow=False)):
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
            copied.append(str(target.relative_to(PROBE)))
    print('\n'.join(copied))
    return 0


def create_review(work, precision=False):
    project = copy_project(work/'projects'/'create-review')
    work.mkdir(parents=True, exist_ok=True)
    method = 'CreatePrecisionClip' if precision else 'CreateReviewController'
    asset = 'MatchPrecision.anim' if precision else 'C_MatchReview.controller'
    code = subprocess.run([str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode',
                           '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.'+method,
                           '-logFile', str(work/'create-review.log')]).returncode
    if code:
        sys.exit(f'CreateReviewController failed ({code}), see {work/"create-review.log"}')
    for suffix in ('', '.meta'):
        relative = 'Assets/Resources/'+asset+suffix
        shutil.copyfile(project/relative, PROBE/relative)
    return 0


def publish(work):
    a, b = work/'native1'/'values.jsonl', work/'native2'/'values.jsonl'
    if not filecmp.cmp(a, b, shallow=False):
        sys.exit('the two Editor captures differ; not publishing')
    rows = [json.loads(line) for line in a.read_text().splitlines() if line.strip()]
    if not rows or rows[-1] != {'k': 'complete', 'v': True} or any(row['k'] == 'error' for row in rows):
        sys.exit('captures did not complete successfully; not publishing')
    if len({row['k'] for row in rows}) != len(rows):
        sys.exit('capture contains duplicate keys; not publishing')
    for repeat in ('native1', 'native2'):
        if (work/repeat/'exit-code.txt').read_text().strip() != '0':
            sys.exit(f'{repeat} did not exit successfully; not publishing')
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
    env = dict(os.environ, MODEL_ROOT_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--headless', '30000', '--no-watch'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'create-review', 'create-precision', 'editor', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'create': create, 'create-review': create_review, 'create-precision': lambda w: create_review(w, True), 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
