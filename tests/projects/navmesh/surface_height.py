#!/usr/bin/env python3
"""Record or check the detail height of a moved or sampled NavMesh point."""
import argparse
import filecmp
import json
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
OUTPUT = 'navmesh.json'


def stimulus(out):
    return {'NAVMESH_PROBE_OUT': str(out/OUTPUT), 'NAVMESH_PROBE_SURFACE_HEIGHT': '1'}


def copy_project(out, *, package_cache=True):
    project = project_copy(HERE, out/'project', package_cache=package_cache)
    (project/'ProjectSettings/ProjectVersion.txt').write_text(f'm_EditorVersion: {unity_version()}\n')
    return project


def capture(path):
    data = json.loads(path.read_text())
    if data.get('height.complete') != {'ok': True}:
        raise RuntimeError(f'{path}: incomplete height capture')
    return data


def editor(root):
    jobs = []
    for repeat in REPEATS:
        out = root/repeat
        project = copy_project(out, package_cache=False)
        jobs.append(EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                              batch=True, env=stimulus(out), timeout=600, label=f'height-{repeat}'))
    failed = False
    for job, result in zip(jobs, run_parallel(jobs)):
        print(summary(result))
        if problem := result_problem(job.out):
            print(f'{job.label}: {problem}')
            failed = True
    return int(failed)


def publish(root, target):
    for repeat in REPEATS:
        out = root/repeat
        if problem := result_problem(out):
            raise RuntimeError(f'{out}: {problem}')
        capture(out/OUTPUT)
    first, second = (root/repeat/OUTPUT for repeat in REPEATS)
    if not filecmp.cmp(first, second, shallow=False):
        raise RuntimeError('the two Editor captures differ')
    target.parent.mkdir(parents=True, exist_ok=True)
    with first.open('rb') as source, target.open('xb') as destination:
        shutil.copyfileobj(source, destination)
    return 0


def gate(root, target):
    project = copy_project(root)
    command = [str(engine_cli()), 'run', '-p', str(project), '--no-watch', '--headless', '120']
    if LAUNCHER.is_file():
        command = [str(LAUNCHER), '--kind', 'engine-cpu', *command]
    with (root/'engine.log').open('w') as log:
        subprocess.run(command, env={**os.environ, **stimulus(root)}, stdout=log,
                       stderr=subprocess.STDOUT, timeout=600, check=True)
    actual = root/OUTPUT
    extra = capture(actual).keys() - capture(target).keys()
    for scenario in sorted(extra):
        print(f'{scenario}: EXTRA in candidate')
    result = subprocess.run([sys.executable, str(HERE/'compare.py'), str(target), str(actual),
                             '--details', str(sys.maxsize)])
    return int(bool(extra) or result.returncode != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'gate'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default=private_label('surface-height'))
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    root = run_root(HERE, args.label).resolve()
    target = reference_root(HERE)/'surface-height.json'
    if args.mode != 'publish':
        root.mkdir(parents=True)
    if args.mode == 'editor':
        return editor(root)
    if args.mode == 'publish':
        return publish(root, target)
    return gate(root, target)


if __name__ == '__main__':
    sys.exit(main())
