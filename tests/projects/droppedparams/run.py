#!/usr/bin/env python3
"""Records the dropped-parameters probe in the Unity Editor or runs it in Unbound.

    run.py create --release R --label L    the Editor makes the probe's assets and scene on a copy,
                                           copied back into Assets/ and ProjectSettings/
    run.py editor --release R --label L    two independent Editor captures (GPU)
    run.py publish --release R --label L   checks they agree byte for byte, writes Reference/
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache; nothing is written here except by `create`. Editors run through
tests/editor_runner.py on the GPU (a private Xvfb and VirtualGL; on macOS arm64 the macOS Editor,
which draws with Metal and publishes Reference/unity-R/metal.jsonl, see tests/probe_paths.py
device_reference).
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import device_reference, editor_path, reference_file, run_root
import editor_runner

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'DROPPED_PARAMS_PROBE_OUT'
CAPTURE = 'capture.jsonl'


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def editor(work):
    jobs = []
    for repeat in ('native1', 'native2'):
        project = copy_project(work/'projects'/repeat)
        out = work/repeat
        if (out/CAPTURE).exists():
            sys.exit(f'refusing to overwrite capture: {out/CAPTURE}')
        jobs.append(editor_runner.EditorJob(project, out/'editor.log', out,
                                            ['-buildTarget', 'Standalone', '-executeMethod', 'ProbeRunner.Record'],
                                            batch=True, env={OUT: str(out/CAPTURE)}, timeout=900,
                                            label='droppedparams-'+repeat))
    results = editor_runner.run_parallel(jobs)
    for result in results:
        print(editor_runner.summary(result))
    return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results))


def create(work):
    project = copy_project(work/'projects'/'create')
    log = work/'create.log'
    code = subprocess.run([str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode',
                           '-nographics', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Create',
                           '-logFile', str(log)]).returncode
    print('editor exit', code)
    if code:
        return code
    shutil.copytree(project/'Assets', PROBE/'Assets', dirs_exist_ok=True)
    for name in ('EditorBuildSettings.asset', 'GraphicsSettings.asset'):
        shutil.copyfile(project/'ProjectSettings'/name, PROBE/'ProjectSettings'/name)
    print('copied the Editor-made assets into', PROBE/'Assets')
    return 0


def publish(work):
    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/CAPTURE, target)
        print('published', target)
    return editor_runner.publish([work/'native1', work/'native2'],
                                 device_reference(reference_file(PROBE, 'jsonl'), PROBE, publishing=True),
                                 lambda d: [CAPTURE], write, gpu=True,
                                 complete=[lambda d: None if (d/CAPTURE).is_file() else f'{CAPTURE} is missing'])


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'capture.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, **{OUT: str(out)})
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--target', 'Standalone', '--no-watch', '--frames', '10', '--size', '64x64'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
