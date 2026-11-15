"""Shared runner for small Play-mode API probes with exact key/value records."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

from editor_runner import EditorJob, publish, result_problem, run_parallel
from probe_paths import reference_root, run_root, shared_runtime

REPO = Path(__file__).resolve().parents[1]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def rows(path):
    values = [json.loads(line) for line in Path(path).read_text().splitlines()]
    keys = [row['key'] for row in values]
    if len(set(keys)) != len(keys):
        raise ValueError('duplicate capture keys')
    return {row['key']: row['value'] for row in values}


def complete(directory):
    try:
        values = rows(directory/'values.jsonl')
    except (OSError, ValueError, KeyError) as error:
        return str(error)
    if values.get('complete') != 'True' or 'fatal' in values or next(reversed(values), None) != 'complete':
        return 'capture is incomplete or has a fatal error'
    return None


def compare(reference, actual, report):
    expected = rows(reference)
    problem = None
    try:
        observed = rows(actual)
    except (OSError, ValueError, KeyError) as error:
        observed, problem = {}, str(error)
    failures = []
    for key in sorted(expected.keys() | observed.keys()):
        if key not in expected or key not in observed or expected[key] != observed[key]:
            failures.append({'key': key, 'reference': expected.get(key), 'actual': observed.get(key)})
    result = {'reference': len(expected), 'matched': sum(key in observed and observed[key] == value for key, value in expected.items()), 'failures': failures, 'capture_problem': problem}
    report.write_text(json.dumps(result, indent=2)+'\n')
    for row in failures:
        print(json.dumps(row))
    print(f"{result['matched']}/{result['reference']} exact API rows; {len(failures)} differences")
    return int(bool(failures) or problem is not None)


def stage(probe, destination, release, configure=None):
    if destination.exists():
        raise ValueError(f'refusing to overwrite {destination}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(probe/part, destination/part)
    (destination/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    if configure:
        configure(destination)
    return destination


def main(probe, output_variable, *, compare_rows=compare, configure=None):
    probe = Path(probe).resolve()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default=f'check-{time.time_ns()}-{os.getpid()}')
    parser.add_argument('--capture-root', type=Path, help='publish: an existing independent capture pair')
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.capture_root and args.mode != 'publish':
        parser.error('--capture-root applies only to publishing')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = args.capture_root or run_root(probe, args.label)
    if args.mode == 'editor':
        jobs = []
        for name in ('native1', 'native2'):
            project = stage(probe, work/name/'project', args.release, configure)
            out = work/name/'out'
            out.mkdir()
            jobs.append(EditorJob(project, out/'Editor.log', out, env={output_variable: str(out)},
                                 timeout=900, progress_files=[out/'values.jsonl'], progress_timeout=600))
        run_parallel(jobs)
        return int(any(result_problem(job.out, gpu=True) or complete(job.out) for job in jobs))
    if args.mode == 'publish':
        directories = [work/name/'out' for name in ('native1', 'native2')]
        def no_forced_exit(directory):
            result = json.loads((directory/'editor-run.json').read_text())
            return 'forced exit' if result.get('forced_exit') else None
        def write(target, first):
            target.mkdir(parents=True)
            shutil.copyfile(first/'values.jsonl', target/'values.jsonl')
        return publish(directories, reference_root(probe), lambda directory: ['values.jsonl'], write,
                       complete=[complete, no_forced_exit], gpu=True)
    work.mkdir(parents=True, exist_ok=False)
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    runtime = shared_runtime(cli, work/'runtime', os.environ.get('UNBOUND_MANAGED_DIR'))
    project = stage(probe, work/'project', args.release, configure)
    out = work/'out'
    out.mkdir()
    env = dict(os.environ, **{output_variable: str(out), 'UNBOUND_MANAGED_DIR': str(runtime/'managed')})
    command = [str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '300', str(runtime/'unbound'),
               'run', '-p', str(project), '--no-watch', '--headless', '120', '--size', '64x32',
               '--player-prefs', str(work/'prefs')]
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run(command, cwd=project, env=env, stdout=log, stderr=subprocess.STDOUT)
    problem = complete(out)
    if result.returncode or problem:
        print(f'engine capture failed: exit {result.returncode}; {problem}; log {out / "unbound.log"}')
    verdict = compare_rows(reference_root(probe)/'values.jsonl', out/'values.jsonl', work/'report.json')
    return int(bool(result.returncode or problem or verdict))
