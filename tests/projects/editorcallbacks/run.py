#!/usr/bin/env python3
"""Records the Editor callback and prefab query probe in the Unity Editor or runs it in Unbound.

    run.py create --release R --label L    the Editor makes the prefabs and scenes in a copy
    run.py editor --release R --label L    two independent Editor captures (native1, native2):
                                           interactive Editors (windows on the private X server),
                                           with --batchmode batch-mode ones
    run.py publish --release R --label L   checks that the two captures agree, copies one to Reference/
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every run works on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/editorcallbacks/<label>; nothing is written here.
Editors run through tests/editor_runner.py: each takes its own GPU lease of the shared launcher,
whose supervisor starts the private Xvfb and VirtualGL inside it, checks the renderer, and stops
the Editor at the hard deadline or when the capture goes silent too long. Do not wrap this
driver in with-heavy-job.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file, run_root
from editor_runner import EditorJob, publish as publish_reference, run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'EDITOR_CALLBACKS_OUT'
# The hard deadline of every Editor run (the earlier runner's watchdog).
TIMEOUT = 900
# The longest silence of values.jsonl, cold import before its first line included. Two parallel
# GPU captures of 6000.0.71f1 loaded their fresh projects in 26.3 s and wrote all 399 lines
# 39 s after start; the budget leaves room for the 140 s cold-refresh outliers seen elsewhere.
PROGRESS_TIMEOUT = 300


def copy_project(dest, authored=True):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    if not authored:
        # The Editor makes these: the prefabs, the scenes and the build settings naming them.
        for part in ('Prefabs', 'Scenes'):
            shutil.rmtree(dest/'Assets'/part, ignore_errors=True)
            (dest/'Assets'/(part+'.meta')).unlink(missing_ok=True)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def run_editors(work, repeats, method, batchmode=True):
    jobs = []
    for repeat in repeats:
        project = copy_project(work/'projects'/repeat, authored=method != 'ProbeSetup.Build')
        out = work/repeat/'values.jsonl'
        out.parent.mkdir(parents=True, exist_ok=True)
        if out.exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        env, progress = {}, {}
        args = ['-executeMethod', method]
        if method == 'ProbeSetup.Build':
            args.append('-quit')
        else:
            env[OUT] = str(out)
            progress = {'progress_files': [out], 'progress_timeout': PROGRESS_TIMEOUT}
        jobs.append(EditorJob(project, work/repeat/'editor.log', work/repeat, args, batch=batchmode,
                              graphics=True, env=env, timeout=TIMEOUT, screen='1920x1080x24',
                              label=repeat, **progress))
    results = run_parallel(jobs)
    for result in results:
        print(summary(result))
    return int(any(r.get('exit_code') != 0 or r.get('forced_exit') for r in results))


def create(work):
    code = run_editors(work, ['created'], 'ProbeSetup.Build')
    print('created project:', work/'projects'/'created')
    return code


def editor(work, batchmode):
    return run_editors(work, ['native1', 'native2'], 'ProbeSetup.Record', batchmode)


def finished(directory):
    """A capture that reached the probe's last step (its done record)."""
    path = Path(directory)/'values.jsonl'
    if not path.exists():
        return 'values.jsonl is missing'
    try:
        rows = [json.loads(line) for line in path.read_text().splitlines() if line.strip()]
    except json.JSONDecodeError:
        return 'values.jsonl is not complete JSON lines'
    if not any(row.get('event') == 'done' for row in rows):
        return 'values.jsonl has no done record'
    return None


def publish(work):
    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/'values.jsonl', target)
        print('published', target)
    return publish_reference([work/'native1', work/'native2'], reference_file(PROBE, 'jsonl'),
                             lambda directory: ['values.jsonl'], write, complete=[finished], gpu=True)


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'values.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, **{OUT: str(out)})
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--headless', '200'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--batchmode', action='store_true', help='batch-mode Editors (editor mode)')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.batchmode)
    return {'create': create, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
