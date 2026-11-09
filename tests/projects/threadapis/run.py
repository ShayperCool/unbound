#!/usr/bin/env python3
"""Records the main-thread probe in the Unity Editor, publishes the reference, runs Unbound.

    run.py editor --repeat native1|native2   one batchmode GPU Editor capture
    run.py publish                           both repeats complete and byte-identical: copies one
                                             to Reference/unity-<release>.jsonl
    run.py unbound --out FILE [--cli PATH]   an Unbound capture ($UNBOUND or `unbound`)

The release is $UNBOUND_PROBE_UNITY_VERSION (default 6000.0.71f1). Editor captures run on fresh
project copies under $XDG_CACHE_HOME/unbound/validation/runs/threadapis/<repeat>; nothing is
written here except by `publish`. Editors go through tests/editor_runner.py (each takes its own
GPU lease; do not wrap this driver in with-heavy-job).
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import reference_file, reference_root, run_root, unity_version  # noqa: E402
from editor_runner import EditorJob, jsonl_complete, publish as publish_reference, run, summary  # noqa: E402

REPEATS = ('native1', 'native2')
OUT = 'values.jsonl'
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def work(repeat):
    return run_root(PROBE, repeat)


def capture_environment(out, followups=False):
    env = {key: value for key, value in os.environ.items()
           if key not in ('THREAD_APIS_OUT', 'THREAD_FOLLOWUP_OUT')}
    env['THREAD_FOLLOWUP_OUT' if followups else 'THREAD_APIS_OUT'] = str(out)
    return env


def editor(repeat, followups=False):
    out = work(repeat)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    project = out/'project'
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, project/part)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    job = EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'], batch=True,
                    env=capture_environment(out/OUT, followups), timeout=900, label='threadapis-'+repeat)
    result = run(job)
    print(summary(result))
    return int(result.get('exit_code') != 0 or bool(result.get('forced_exit')))


def publish(repeats=REPEATS):
    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/OUT, target)
        print('published', target)
    return publish_reference([work(r) for r in repeats], reference_file(PROBE, 'jsonl'), lambda _: [OUT], write,
                             complete=[jsonl_complete(OUT, {'complete': True})], gpu=True)


def publish_raycast(repeats):
    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/'raycast.jsonl', target)
        print('published', target)
    return publish_reference([work(r) for r in repeats], reference_root(PROBE)/'raycast.jsonl',
                             lambda _: [OUT, 'raycast.jsonl'], write,
                             complete=[jsonl_complete('raycast.jsonl', {'complete': True})], gpu=True)


def unbound(out, cli, followups=False):
    cli = cli or os.environ.get('UNBOUND') or 'unbound'
    command = [cli, 'run', '-p', str(PROBE), '--headless', '30', '--no-watch']
    if LAUNCHER.exists():
        command = [str(LAUNCHER), '--kind', 'engine-cpu', *command]
    return subprocess.call(command, env=capture_environment(Path(out).resolve(), followups))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=('editor', 'publish', 'publish-raycast', 'publish-followups', 'unbound'))
    parser.add_argument('--repeat')
    parser.add_argument('--followups', action='store_true')
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--out')
    parser.add_argument('--cli')
    args = parser.parse_args()
    if args.command == 'editor':
        if not args.repeat:
            parser.error('editor needs --repeat')
        return editor(args.repeat, args.followups)
    if args.command == 'publish':
        return publish(tuple(args.labels) or REPEATS)
    if args.command == 'publish-followups':
        def write(target, first):
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(first/OUT, target)
        return publish_reference([work(r) for r in args.labels], reference_root(PROBE)/'followups.jsonl',
                                 lambda _: [OUT], write, complete=[jsonl_complete(OUT, {'complete': True})], gpu=True)
    if args.command == 'publish-raycast':
        return publish_raycast(tuple(args.labels))
    if not args.out:
        parser.error('unbound needs --out')
    return unbound(args.out, args.cli, args.followups)


if __name__ == '__main__':
    sys.exit(main())
