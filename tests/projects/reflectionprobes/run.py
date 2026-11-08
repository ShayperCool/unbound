#!/usr/bin/env python3
"""Records the reflection probe probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L    two independent batchmode GPU Editor captures
    run.py check   --release R --label L    validates the two Editor captures
    run.py publish --release R --label L    checks that the captures agree and writes
                                            Reference/unity-R (refuses to overwrite)
    run.py unbound --release R --label L    an Unbound capture (UNBOUND_CLI: the binary)
    run.py gate    --release R              an Unbound capture compared with Reference/unity-R

Every capture runs on a fresh copy of Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/reflectionprobes/<label>; nothing is written here except by
publish. Each Editor runs through tests/editor_runner.py (one launcher lease per repeat, a private
Xvfb, the GPU through VirtualGL, the renderer checked); do not wrap this driver in with-heavy-job.
Publishing requires each capture to end with `complete: true` and the two repeats to be
byte-identical.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root
from editor_runner import EditorJob, load_result, publish as publish_repeats, result_problem, \
    run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
PROGRESS_TIMEOUT = 600
HARD_TIMEOUT = 1200


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def fresh_output(directory):
    if directory.exists():
        sys.exit(f'refusing to overwrite capture: {directory}')
    directory.mkdir(parents=True)
    return directory


def outputs(directory):
    return sorted(p.name for p in directory.iterdir() if p.suffix in ('.png', '.bin')) + ['values.jsonl']


def job(work, repeat):
    project, out = work/'projects'/repeat, work/repeat
    return EditorJob(project, out/'editor.log', out,
                     ['-executeMethod', 'ReflectionProbesRunner.Record'], batch=True,
                     env={'REFLECTION_PROBES_OUT': str(out)},
                     timeout=HARD_TIMEOUT, progress_files=[out/'values.jsonl'],
                     progress_timeout=PROGRESS_TIMEOUT, label='reflectionprobes-'+repeat)


def editor(work):
    jobs = [job(work, repeat) for repeat in REPEATS]
    for j in jobs:
        copy_project(j.project)
        fresh_output(j.out)
    results = run_parallel(jobs)
    for result in results:
        print(summary(result))
    return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results))


def capture_problem(repeat):
    values = repeat/'values.jsonl'
    if not values.exists():
        return 'the capture did not finish'
    rows = [json.loads(line) for line in values.read_text().splitlines() if line.strip()]
    keys = [r['k'] for r in rows]
    if not rows or rows[-1] != {'k': 'complete', 'v': True}:
        return 'the probe did not record complete: true last'
    if any(k.endswith('.fatal') for k in keys):
        return 'the probe recorded a fatal error'
    if len(set(keys)) != len(keys):
        return 'duplicate keys'
    return None


def check(work):
    status = 0
    for repeat in (work/r for r in REPEATS):
        reason = result_problem(repeat, gpu=True) or capture_problem(repeat)
        print(repeat.name, reason or 'complete')
        status |= reason is not None
    return int(status)


def write_reference(target, first):
    target.mkdir(parents=True)
    for name in outputs(first):
        shutil.copyfile(first/name, target/name)
    print('published', target, len(outputs(first)), 'files')


def publish(work):
    repeats = [work/r for r in REPEATS]
    return publish_repeats(repeats, reference_root(PROBE), outputs, write_reference,
                           complete=[capture_problem], gpu=True)


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = fresh_output(work/'unbound')
    env = dict(os.environ, REFLECTION_PROBES_OUT=str(out))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '2000', '--size', '320x180'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'check', 'gate'))
    parser.add_argument('--label')
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.mode == 'gate':
        os.environ.setdefault('UNBOUND_CLI', os.environ.get('UNBOUND', str(REPO/'target/release/unbound')))
        work = run_root(PROBE, 'gate-'+str(time.time_ns()))
        status = unbound(work)
        status |= subprocess.call([sys.executable, str(PROBE/'compare.py'), str(reference_root(PROBE)), str(work/'unbound')])
        return int(status != 0)
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work)
    if args.mode == 'unbound':
        return unbound(work)
    if args.mode == 'publish':
        return publish(work)
    return check(work)


if __name__ == '__main__':
    sys.exit(main())
