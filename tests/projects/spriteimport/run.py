#!/usr/bin/env python3
"""Records the sprite import probe in the Unity Editor or runs it in Unbound.

    run.py create [--label L]    makes Assets/Scenes/Probe.unity (and the Editor's new .meta files)
    run.py editor [--label L]    two independent Editor captures
    run.py publish [--label L]   checks the two captures agree, copies one to Reference/
    run.py unbound [--label L]   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache; only `create` copies files back (the scene, its build settings entry and the metas
the Editor made for new files). Editors run through the shared heavy-job launcher.
"""
import argparse
import filecmp
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
        shutil.rmtree(dest)
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
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
    jobs = []
    for repeat in ('native1', 'native2'):
        project = copy_project(work/'projects'/repeat)
        out = work/repeat/'sprites.jsonl'
        out.parent.mkdir(parents=True, exist_ok=True)
        out.unlink(missing_ok=True)
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
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'sprites.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    out.unlink(missing_ok=True)
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '10', '--size', '64x64'],
                                env=dict(os.environ, SPRITE_IMPORT_PROBE_OUT=str(out)),
                                stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    parser.add_argument('--label', default='current')
    args = parser.parse_args()
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
