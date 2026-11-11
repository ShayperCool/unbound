#!/usr/bin/env python3
"""Records the OnRenderObject order witness in the Unity Editor or runs it in Unbound.

    run.py editor --release R --label L --repeat native1   one windowed GPU Editor capture
    run.py editor --release R --label L --repeat native2   the independent repeat (only after
                                                           native1 passed `check`)
    run.py check --release R --label L --repeat native1    the capture's guards (analyze.py guards)
    run.py publish --release R --label L                   both repeats pass, byte-identical:
                                                           copies one to Reference/
    run.py unbound --release R --label L                   an Unbound capture (UNBOUND_CLI)
    run.py editor ... --dry-run                            prints the exact Editor job, starts nothing

The project is the core API probe's (scene, assets, RenderWatch handlers, ProbeRunner), with
CoreProbe extended by the order window (see README.md). Every capture runs on a fresh copy under
$XDG_CACHE_HOME/unbound/validation/runs/oroorder/<label>; nothing is written here. Editors run
through tests/editor_runner.py (each takes its own GPU lease; do not wrap this driver in
with-heavy-job). The repeats run one after the other, never in parallel.
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
from editor_runner import vulkan_display_env, EditorJob, jsonl_complete, publish as publish_reference, result_problem, run, summary

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
# The core API probe's hard deadline (its runner's `timeout -s KILL 900`). The probe writes its
# record once, at the end, so output-progress monitoring stays off.
HARD_TIMEOUT = 900
MARKER = {'k': 'complete', 'v': True}

sys.path.insert(0, str(PROBE))
import analyze  # noqa: E402


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def job(work, repeat):
    project, out = work/'projects'/repeat, work/repeat
    return EditorJob(project, out/'editor.log', out, private_display=False, env={**vulkan_display_env(), 'CORE_APIS_OUT': str(out/'values.jsonl')},
                     timeout=HARD_TIMEOUT, label='oroorder-'+repeat,
                     launch_inputs={'release': os.environ['UNBOUND_PROBE_UNITY_VERSION'],
                                    'paths': {'assets': str(project/'Assets'),
                                              'packages': str(project/'Packages'),
                                              'settings': str(project/'ProjectSettings'),
                                              'runner': str(Path(__file__).resolve()),
                                              'analyzer': str(PROBE/'analyze.py'),
                                              'supervisor': str(REPO/'tests/editor_runner.py'),
                                              'profile': str(REPO/'data/profiles/unity-6000.0.json')}})


def editor(work, repeat, dry_run=False):
    j = job(work, repeat)
    if dry_run:
        print(json.dumps({'label': j.label, 'kind': j.kind, 'command': j.command(), 'env': j.env,
                          'timeout': j.timeout, 'project': str(j.project), 'out': str(j.out),
                          'launch_inputs': j.launch_inputs}, indent=1))
        return 0
    if repeat == 'native2':
        problem = capture_problem(work/'native1')
        if problem:
            sys.exit(f'native1 did not pass ({problem}); the repeat is not admitted')
    if j.out.exists():
        sys.exit(f'refusing to overwrite capture: {j.out}')
    copy_project(j.project)
    j.out.mkdir(parents=True)
    result = run(j)
    print(summary(result))
    return int(result.get('exit_code') != 0 or bool(result.get('forced_exit')))


def strict_result(directory):
    """Strict exit 0: no forced stop of any kind (not even the shutdown guard)."""
    result = json.loads((Path(directory)/'editor-run.json').read_text()) \
        if (Path(directory)/'editor-run.json').exists() else None
    if result is None:
        return 'no Editor result'
    if result.get('forced_exit'):
        return f"forced exit {result['forced_exit']}"
    if result.get('exit_code') != 0:
        return f"exit {result.get('exit_code')}"
    return result_problem(directory, gpu=True)


def capture_problem(directory):
    return (strict_result(directory) or jsonl_complete('values.jsonl', MARKER)(directory)
            or analyze.guard_problem(Path(directory)/'values.jsonl'))


def check(work, repeat):
    problem = capture_problem(work/repeat)
    print(f'{repeat}:', problem or 'passes')
    return int(bool(problem))


def publish(work):
    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/'values.jsonl', target)
        print('published', target)
    for repeat in REPEATS:
        problem = capture_problem(work/repeat)
        if problem:
            sys.exit(f'{repeat}: {problem}; not publishing')
    return publish_reference([work/r for r in REPEATS], reference_file(PROBE, 'jsonl'),
                             lambda directory: ['values.jsonl'], write,
                             complete=[jsonl_complete('values.jsonl', MARKER)], gpu=True)


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'values.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, CORE_APIS_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '220', '--size', '320x240'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'check', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--repeat', choices=REPEATS)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.mode in ('editor', 'check') and not args.repeat:
        parser.error('--repeat is required')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.repeat, args.dry_run)
    if args.mode == 'check':
        return check(work, args.repeat)
    return {'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
