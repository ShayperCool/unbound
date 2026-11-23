#!/usr/bin/env python3
"""Record or check the NavLinks movement, projection, staleness, and overlap controls."""
import argparse
import filecmp
import os
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))
from editor_runner import EditorJob, LAUNCHER, result_problem, run_parallel, summary
from probe_paths import private_label, project_copy, reference_root, run_root, unity_version
from value_probe import engine_cli

REPEATS = ('native1', 'native2')
OUTPUT = 'values.jsonl'
PROFILES = {
    'projection': ('NAVLINKS_PROJECTION', 'point-projection.jsonl', 20),
    'staleness': ('NAVLINKS_STALE', 'path-staleness.jsonl', 30000),
    'overlap': ('NAVLINKS_OVERLAP', 'crowd-overlap.jsonl', 300),
    'wallmove': ('NAVLINKS_WALL_MOVE', 'wall-movement.jsonl', 500),
    'linkversion': ('NAVLINKS_LINK_VERSION', 'link-version.jsonl', 2000),
    'assignment': ('NAVLINKS_ASSIGNMENT', 'pending-assignment.jsonl', 1700),
}


def stimulus(out, profile):
    return {'NAVLINKS_OUT': str(out/OUTPUT),
            **{variable: str(int(name == profile)) for name, (variable, _, _) in PROFILES.items()}}


def copy_project(destination, *, package_cache=True):
    project = project_copy(HERE, destination, package_cache=package_cache)
    version = project/'ProjectSettings/ProjectVersion.txt'
    version.write_text(f'm_EditorVersion: {unity_version()}\n')
    return project


def editor(root, profile):
    jobs = []
    for repeat in REPEATS:
        out = root/repeat
        project = copy_project(out/'project', package_cache=False)
        jobs.append(EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                              batch=True, env=stimulus(out, profile), timeout=600, label=f'{profile}-{repeat}'))
    failed = False
    for job, result in zip(jobs, run_parallel(jobs)):
        print(summary(result))
        if (problem := result_problem(job.out)):
            print(f'{job.label}: {problem}')
            failed = True
    return int(failed)


def publish(root, target):
    for repeat in REPEATS:
        out = root/repeat
        if (problem := result_problem(out)):
            raise RuntimeError(f'{out}: {problem}')
    first, second = (root/repeat/OUTPUT for repeat in REPEATS)
    if not filecmp.cmp(first, second, shallow=False):
        raise RuntimeError('the two Editor captures differ')
    target.parent.mkdir(parents=True, exist_ok=True)
    with first.open('rb') as source, target.open('xb') as reference:
        shutil.copyfileobj(source, reference)
    return 0


def gate(root, target, profile):
    project = copy_project(root/'project')
    frames = PROFILES[profile][2]
    command = [str(engine_cli()), 'run', '-p', str(project), '--headless', str(frames), '--no-watch']
    if LAUNCHER.is_file():
        command = [str(LAUNCHER), '--kind', 'engine-cpu', *command]
    with (root/'engine.log').open('w') as log:
        result = subprocess.run(command, env={**os.environ, **stimulus(root, profile)},
                                stdout=log, stderr=subprocess.STDOUT, timeout=600)
    if result.returncode:
        raise RuntimeError(f'Unbound exited {result.returncode}, see {log.name}')
    return subprocess.run([sys.executable, str(HERE/'compare.py'), str(target), str(root/OUTPUT),
                           '--details', str(sys.maxsize), '--report', str(root/'report.jsonl')]).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'gate'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--profile', choices=PROFILES, default='projection')
    parser.add_argument('--label')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    root = run_root(HERE, args.label or private_label(args.profile)).resolve()
    target = reference_root(HERE)/PROFILES[args.profile][1]
    if args.mode != 'publish':
        root.mkdir(parents=True)
    if args.mode == 'editor':
        return editor(root, args.profile)
    if args.mode == 'publish':
        return publish(root, target)
    return gate(root, target, args.profile)


if __name__ == '__main__':
    sys.exit(main())
