#!/usr/bin/env python3
"""Core system capabilities and low-level memory page controls on Linux OpenGL Core and Vulkan.

    run.py editor  --release R --device opengl|vulkan --label L
    run.py publish --release R --device opengl|vulkan --label L
    run.py unbound --release R --device opengl|vulkan [--label L]

Run output goes to $XDG_CACHE_HOME/unbound/validation/runs/corecaps/<label>.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run, summary, result_problem  # noqa: E402
from probe_paths import run_root  # noqa: E402

REPEATS = ('native1', 'native2')
COMPARE = PROBE / 'compare.py'


def prepare(project, release):
    if project.exists():
        raise SystemExit(f'project already exists: {project}')
    base = REPO / 'tests/projects/lifecycle'
    for part in ('ProjectSettings', 'Packages'):
        shutil.copytree(base / part, project / part)
    shutil.copytree(PROBE / 'Assets', project / 'Assets')
    shutil.copytree(base / 'Assets/Scenes', project / 'Assets/Scenes')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + release + '\n')
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {}}))


def capture_problem(directory):
    problem = result_problem(directory, gpu=True)
    if problem:
        return problem
    result = json.loads((directory / 'editor-run.json').read_text())
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 'capture did not exit normally'
    values = directory / 'values.jsonl'
    if not values.exists():
        return 'no record'
    rows = [json.loads(line) for line in values.read_text().splitlines() if line.strip()]
    if not rows or rows[-1] != {'k': 'complete', 'v': True}:
        return 'incomplete record'
    if any(row['k'] == 'error' for row in rows):
        return 'the probe logged an error'
    if len({row['k'] for row in rows}) != len(rows):
        return 'duplicate keys'
    return None


def make_job(project, out, release, device):
    return EditorJob(
        project, out / 'editor.log', out,
        args=['-executeMethod', 'ProbeRunner.Record'],
        batch=True, graphics=True, vulkan=(device == 'vulkan'),
        timeout=900, label=f'corecaps-{device}-{out.name}',
        env={'CORECAPS_OUT': str(out / 'values.jsonl')},
        launch_inputs={'release': release, 'device': device, 'paths': {
            'assets': str(project / 'Assets'), 'settings': str(project / 'ProjectSettings'),
            'packages': str(project / 'Packages'), 'runner': str(PROBE / 'run.py'),
            'supervisor': str(REPO / 'tests/editor_runner.py')}},
    )


def compare(reference, actual, device, no_residuals=False):
    cmd = [sys.executable, str(COMPARE), str(reference), str(actual), '--device', device]
    if no_residuals:
        cmd.append('--no-residuals')
    return subprocess.run(cmd).returncode


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    p.add_argument('--release', required=True)
    p.add_argument('--device', choices=('opengl', 'vulkan'), default='opengl')
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    args = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    reference = PROBE / 'Reference' / f'unity-{args.release}' / f'{args.device}.jsonl'
    if args.mode == 'editor':
        jobs = []
        for repeat in REPEATS:
            project, out = work / 'projects' / repeat, work / repeat
            prepare(project, args.release)
            jobs.append(make_job(project, out, args.release, args.device))
        for result in [run(j) for j in jobs]:
            print(summary(result))
        problems = [f'{r}: {capture_problem(work / r)}' for r in REPEATS if capture_problem(work / r)]
        if problems:
            raise SystemExit('; '.join(problems))
        return compare(work / 'native1/values.jsonl', work / 'native2/values.jsonl', args.device, no_residuals=True)
    if args.mode == 'publish':
        for repeat in REPEATS:
            problem = capture_problem(work / repeat)
            if problem:
                raise SystemExit(f'invalid capture: {repeat}: {problem}')
        first, second = (work / r / 'values.jsonl' for r in REPEATS)
        if first.read_bytes() != second.read_bytes():
            raise SystemExit('the two captures differ')
        reference.parent.mkdir(parents=True, exist_ok=True)
        with reference.open('xb') as stream:
            stream.write(first.read_bytes())
        return 0
    project = work / 'projects/unbound'
    prepare(project, args.release)
    out = work / 'unbound'
    out.mkdir()
    cli = os.environ.get('UNBOUND', os.environ.get('UNBOUND_CLI', str(REPO / 'target/release/unbound')))
    device_flag = ['--force-vulkan'] if args.device == 'vulkan' else ['--force-glcore']
    with (out / 'engine.log').open('w') as log:
        code = subprocess.run(
            [str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
             cli, 'run', '-p', str(project), '--frames', '120', '--size', '320x240', '--no-watch', *device_flag],
            env=dict(os.environ, CORECAPS_OUT=str(out / 'values.jsonl')),
            stdout=log, stderr=subprocess.STDOUT).returncode
    if code:
        print((out / 'engine.log').read_text()[-3000:])
        return code
    return compare(reference, out / 'values.jsonl', args.device)


if __name__ == '__main__':
    sys.exit(main())
