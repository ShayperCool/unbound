#!/usr/bin/env python3
"""Bakes, records or runs the light probe API probe.

    run.py create --release R --label L    the Editor builds and bakes Assets/Scenes (Main, Extra) on a
                                           copy; the scenes, bakes and metas are copied back here
    run.py editor --release R --label L    two independent Editor captures
    run.py publish --release R --label L   checks the two captures agree, copies one to Reference/
    run.py unbound --release R --label L   one Unbound capture (UNBOUND_CLI: the binary)
    run.py lifecycle --release R --label L          two independent Editor captures of the scene
                                                    unload event-order witness (Lifecycle/)
    run.py lifecycle-publish --release R --label L  checks they are complete, strict exit 0 and
                                                    identical; writes Reference/.../lifecycle.jsonl

Every capture runs on a fresh copy of this project in the validation cache. Editors run through
the shared heavy-job launcher on the GPU (a private Xvfb and VirtualGL); do not wrap this driver.
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
import editor_runner

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
    return dest


def display():
    """A private X server for the Editors, which draw with the GPU through VirtualGL."""
    for number in range(120, 161):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def editor_command(project, method, log):
    return [str(LAUNCHER), 'timeout', '-s', 'KILL', '1200', 'vglrun', '-d', 'egl', str(editor_path()),
            '-batchmode', '-buildTarget', 'Standalone', '-projectPath', str(project),
            '-executeMethod', method, '-logFile', str(log)]


def create(work):
    project = copy_project(work/'projects'/'create')
    name, server = display()
    try:
        code = subprocess.run(editor_command(project, 'LightProbesSetup.Create', work/'create.log'),
                              env=dict(os.environ, DISPLAY=name)).returncode
    finally:
        server.terminate()
    print('editor exit', code)
    if code:
        return code
    shutil.copytree(project/'Assets/Scenes', PROBE/'Assets/Scenes', dirs_exist_ok=True)
    for meta in ('Scenes.meta', 'Editor.meta', 'Scripts.meta'):
        shutil.copyfile(project/'Assets'/meta, PROBE/'Assets'/meta)
    for folder in ('Editor', 'Scripts'):
        for meta in (project/'Assets'/folder).glob('*.meta'):
            shutil.copyfile(meta, PROBE/'Assets'/folder/meta.name)
    shutil.copyfile(project/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
    print('copied the Editor-made scenes, bakes and metas into', PROBE/'Assets')
    return 0


def editor(work):
    name, server = display()
    try:
        jobs = []
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = work/repeat/'probe.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            if out.exists():
                sys.exit(f'refusing to overwrite capture: {out}')
            env = dict(os.environ, DISPLAY=name, LIGHT_PROBES_PROBE_OUT=str(out))
            jobs.append(subprocess.Popen(editor_command(project, 'ProbeRunner.Record', work/repeat/'editor.log'), env=env))
        codes = [job.wait() for job in jobs]
        print('editor exits', codes)
        return int(any(codes))
    finally:
        server.terminate()


def publish(work):
    target = reference_root(PROBE)
    target.mkdir(parents=True, exist_ok=True)
    for name in ('probe.jsonl', 'probe.png'):
        a, b = work/'native1'/name, work/'native2'/name
        if not filecmp.cmp(a, b, shallow=False):
            sys.exit(f'{name}: the two Editor captures differ; not publishing')
    for log in (work/'native1'/'editor.log', work/'native2'/'editor.log'):
        if 'NVIDIA' not in next((l for l in open(log, errors='replace') if 'Renderer:' in l), ''):
            sys.exit(f'{log}: the Editor did not render on the NVIDIA GPU; not publishing')
    for name in ('probe.jsonl', 'probe.png'):
        shutil.copyfile(work/'native1'/name, target/name)
        print('published', target/name)
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'probe.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, LIGHT_PROBES_PROBE_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--target', 'Standalone', '--no-watch', '--frames', '40', '--size', '320x180'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode)
    return result.returncode


LIFECYCLE = 'lifecycle.jsonl'
LIFECYCLE_TIMEOUT = 1200   # hard deadline per admitted Editor
# Silence budget, including the cold import before the witness creates its output (the main
# probe's fresh-copy captures import in about 10 s and finish in about a minute).
LIFECYCLE_PROGRESS = 600


def lifecycle_project(dest):
    """A fresh copy of the fixture (every scene, bake, meta and setting) in which the witness
    replaces the main probe's recorder: LightProbesProbe quits the Editor when its own output
    variable is unset, and no scene references it."""
    project = copy_project(dest)
    for name in ('LightProbesProbe.cs', 'LightProbesProbe.cs.meta'):
        (project/'Assets/Scripts'/name).unlink()
    for name in ('LightProbesLifecycle.cs', 'LightProbesLifecycle.cs.meta'):
        shutil.copyfile(PROBE/'Lifecycle'/name, project/'Assets/Scripts'/name)
    return project


def lifecycle(work, release):
    jobs = []
    for repeat in ('native1', 'native2'):
        out = work/repeat
        if (out/LIFECYCLE).exists() or (out/editor_runner.RESULT).exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        project = lifecycle_project(work/'projects'/repeat)
        out.mkdir(parents=True, exist_ok=True)
        inputs = {'release': release, 'paths': {
            'project/Assets': str(project/'Assets'), 'project/Packages': str(project/'Packages'),
            'project/ProjectSettings': str(project/'ProjectSettings'),
            'probe/Lifecycle': str(PROBE/'Lifecycle'), 'probe/run.py': str(PROBE/'run.py'),
            'tests/editor_runner.py': str(REPO/'tests/editor_runner.py'),
            'tests/probe_paths.py': str(REPO/'tests/probe_paths.py'),
            'profile': str(REPO/'data/profiles'/('unity-'+'.'.join(release.split('.')[:2])+'.json'))}}
        jobs.append(editor_runner.EditorJob(
            project, out/'editor.log', out, ['-buildTarget', 'Standalone', '-executeMethod', 'ProbeRunner.Record'],
            batch=True, env={'LIGHT_PROBES_LIFECYCLE_OUT': str(out/LIFECYCLE)}, timeout=LIFECYCLE_TIMEOUT,
            progress_files=[out/LIFECYCLE], progress_timeout=LIFECYCLE_PROGRESS, label='lifecycle-'+repeat,
            launch_inputs=inputs))
    results = editor_runner.run_parallel(jobs)
    for result in results:
        print(editor_runner.summary(result))
    problems = [lifecycle_problem(work/r) for r in ('native1', 'native2')]
    for repeat, problem in zip(('native1', 'native2'), problems):
        print(repeat, problem or 'complete, exit 0, no forced exit')
    return int(any(problems))


def lifecycle_problem(directory):
    """Strict: exit 0 without any forced stop, the NVIDIA renderer, a complete witness."""
    result = editor_runner.load_result(directory)
    if result is None:
        return 'no Editor result'
    if result.get('forced_exit'):
        return f"forced exit ({result['forced_exit']}: {result.get('forced_detail')})"
    return editor_runner.result_problem(directory, gpu=True) or editor_runner.jsonl_complete(LIFECYCLE)(directory)


def lifecycle_publish(work):
    for repeat in ('native1', 'native2'):
        problem = lifecycle_problem(work/repeat)
        if problem:
            sys.exit(f'{repeat}: {problem}; not publishing')
    return editor_runner.publish(
        [work/'native1', work/'native2'], reference_root(PROBE)/LIFECYCLE, lambda d: [LIFECYCLE],
        lambda target, first: (shutil.copyfile(first/LIFECYCLE, target), print('published', target)),
        complete=[editor_runner.jsonl_complete(LIFECYCLE)], gpu=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound', 'lifecycle', 'lifecycle-publish'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'lifecycle':
        return lifecycle(work, args.release)
    if args.mode == 'lifecycle-publish':
        return lifecycle_publish(work)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
