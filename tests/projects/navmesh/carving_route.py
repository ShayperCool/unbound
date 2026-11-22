#!/usr/bin/env python3
"""Record or check the blocked agent's route when a parked carving hole is removed."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))
from editor_runner import EditorJob, LAUNCHER, result_problem, run_parallel, summary
from probe_paths import private_label, project_copy, reference_root, run_root
from value_probe import engine_cli

REPEATS = ('native1', 'native2')
MODES = ('remove', 'keep')
OUTPUT = 'navmesh.json'


def stimulus(mode, out):
    return {'NAVMESH_PROBE_OUT': str(out/OUTPUT), 'NAVMESH_PROBE_CARVING_ROUTE': mode}


def scenarios(path, mode):
    prefix = f'carvingRoute.{mode}.f'
    data = json.loads(path.read_text())
    values = {scenario: fields for scenario, fields in data.items() if scenario.startswith(prefix)}
    expected = {f'{prefix}{frame}' for frame in range(228, 251)}
    if values.keys() != expected:
        missing = sorted(expected - values.keys())
        extra = sorted(values.keys() - expected)
        raise RuntimeError(f'{path}: missing {missing}, extra {extra}')
    return values


def editor(root):
    jobs = []
    for mode in MODES:
        for repeat in REPEATS:
            out = root/mode/repeat
            project = project_copy(HERE, out/'project', package_cache=False)
            jobs.append(EditorJob(project, out/'editor.log', out,
                                  ['-executeMethod', 'ProbeRunner.Record'], batch=True,
                                  env=stimulus(mode, out), timeout=1200, label=f'carve-{mode}-{repeat}'))
    failed = False
    for job, result in zip(jobs, run_parallel(jobs)):
        print(summary(result))
        if (problem := result_problem(job.out)):
            print(f'{job.label}: {problem}')
            failed = True
    return int(failed)


def publish(root, target):
    reference = {}
    for mode in MODES:
        values = []
        for repeat in REPEATS:
            out = root/mode/repeat
            if (problem := result_problem(out)):
                raise RuntimeError(f'{out}: {problem}')
            values.append(scenarios(out/OUTPUT, mode))
        if values[0] != values[1]:
            raise RuntimeError(f'{mode}: the two Editor captures differ')
        reference.update(values[0])
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open('x') as output:
        output.write(json.dumps(reference, indent=2)+'\n')
    return 0


def gate(root, target):
    candidate = {}
    cli = engine_cli()
    for mode in MODES:
        out = root/mode/'unbound'
        project = project_copy(HERE, out/'project')
        command = [str(cli), 'run', '-p', str(project), '--no-watch', '--frames', '300']
        if LAUNCHER.is_file():
            command = [str(LAUNCHER), '--kind', 'engine-gpu', *command]
        with (out/'engine.log').open('w') as log:
            result = subprocess.run(command, env={**os.environ, **stimulus(mode, out)},
                                    stdout=log, stderr=subprocess.STDOUT, timeout=900)
        if result.returncode:
            raise RuntimeError(f'{mode}: Unbound exited {result.returncode}, see {log.name}')
        candidate.update(scenarios(out/OUTPUT, mode))
    actual = root/target.name
    actual.write_text(json.dumps(candidate, indent=2)+'\n')
    return subprocess.run([sys.executable, str(HERE/'compare.py'), str(target), str(actual),
                           '--details', str(sys.maxsize)]).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'gate'))
    parser.add_argument('--label', default=private_label('carving-route'))
    args = parser.parse_args()
    root = run_root(HERE, args.label).resolve()
    target = reference_root(HERE)/'carving-route.json'
    if args.mode != 'publish':
        root.mkdir(parents=True)
    if args.mode == 'editor':
        return editor(root)
    if args.mode == 'publish':
        return publish(root, target)
    return gate(root, target)


if __name__ == '__main__':
    sys.exit(main())
