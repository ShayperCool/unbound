#!/usr/bin/env python3
"""Records the sprite import probe in the Unity Editor or runs it in Unbound.

    run.py create --release VERSION --label L    makes Assets/Scenes/Probe.unity
    run.py editor --release VERSION --label L    two independent Editor captures
    run.py publish --release VERSION --label L   checks agreement, copies one to Reference/
    run.py unbound --release VERSION --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache; only `create` copies files back (the scene, its build settings entry and the metas
the Editor made for new files). Editors run through the shared heavy-job launcher.
"""
import argparse
import filecmp
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_file, run_root, unity_version

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def copy_project(dest):
    if dest.exists():
        raise FileExistsError(f'Capture project exists; choose a fresh label: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    return dest


def run_editor(project, method, log, env=None, quit=False):
    return subprocess.Popen(
        [str(LAUNCHER), 'timeout', '-s', 'KILL', '1200', str(editor_path()), '-batchmode', '-nographics',
         *(['-quit'] if quit else []),
         '-projectPath', str(project), '-executeMethod', method, '-logFile', str(log)],
        env=dict(os.environ, **(env or {})))


def create(work):
    project = copy_project(work/'projects'/'create')
    code = run_editor(project, 'ProbeRunner.Create', work/'create.log', quit=True).wait()
    print('editor exit', code)
    if code:
        return code
    copied = []
    for path in sorted((project/'Assets').rglob('*')):
        rel = path.relative_to(project)
        mine = PROBE/rel
        if path.is_file() and (not mine.exists() or rel.name == 'Probe.unity'):
            mine.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, mine)
            copied.append(str(rel))
        elif path.is_file() and path.suffix == '.meta' and not filecmp.cmp(path, mine, shallow=False):
            print('the Editor rewrote', rel, '(kept ours)')
    settings = Path('ProjectSettings/EditorBuildSettings.asset')
    if not filecmp.cmp(project/settings, PROBE/settings, shallow=False):
        shutil.copyfile(project/settings, PROBE/settings)
        copied.append(str(settings))
    print('copied', *copied, sep='\n  ')
    return 0


def editor(work):
    for repeat in ('native1', 'native2'):
        if (work/repeat).exists() or (work/'projects'/repeat).exists():
            raise FileExistsError(f'Native capture exists; choose a fresh label: {work/repeat}')
    jobs = []
    for repeat in ('native1', 'native2'):
        project = copy_project(work/'projects'/repeat)
        out = work/repeat/'sprites.jsonl'
        out.parent.mkdir(parents=True, exist_ok=True)
        jobs.append(run_editor(project, 'ProbeRunner.Record', work/repeat/'editor.log',
                               {'SPRITE_IMPORT_PROBE_OUT': str(out)}))
    codes = [job.wait() for job in jobs]
    print('editor exits', codes)
    return int(any(codes))


def publish(work):
    a, b = work/'native1'/'sprites.jsonl', work/'native2'/'sprites.jsonl'
    if not filecmp.cmp(a, b, shallow=False):
        sys.exit('the two Editor captures differ; not publishing')
    target = reference_file(PROBE, 'jsonl')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(a, target)
    print('published', target)
    return 0


def unbound(work):
    if (work/'unbound').exists():
        raise FileExistsError(f'Engine capture exists; choose a fresh label: {work}')
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'sprites.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    with (work/'unbound'/'unbound.log').open('x') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '10', '--size', '64x64'],
                                env=dict(os.environ, SPRITE_IMPORT_PROBE_OUT=str(out)),
                                stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]*', args.label):
        parser.error('--label must be a single fresh directory name')
    if not re.fullmatch(r'\d+\.\d+\.\d+[abfp]\d+', args.release):
        parser.error('--release must be an Editor version, such as 6000.0.71f1')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    release_file = work/'release.txt'
    if release_file.exists():
        if release_file.read_text().strip() != args.release:
            parser.error('this label belongs to a different Editor release')
    else:
        release_file.write_text(args.release+'\n')
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
