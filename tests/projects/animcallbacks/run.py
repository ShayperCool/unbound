#!/usr/bin/env python3
"""Animation callback placement in the player loop, with private authored project copies.

    run.py create  --release R                 authors the clips and controllers (batchmode Editor)
    run.py editor  --release R --label L       two independent windowed GPU Editor captures
    run.py publish --release R --label L       publishes the identical captures to Reference/unity-R.jsonl
    run.py unbound --release R [--label L]     runs Unbound and compares strictly with Reference/

Run output goes to $XDG_CACHE_HOME/unbound/validation/runs/animcallbacks/<label>.
"""
import argparse
import difflib
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
from editor_runner import EditorJob, run, run_parallel, summary, result_problem  # noqa: E402
from probe_paths import run_root  # noqa: E402

REPEATS = ('native1', 'native2')
# The humanoid model and clip shared with the culling probe (their .meta files keep the GUIDs
# the authored controllers reference).
SHARED = ('HumanA.fbx', 'LoopMuscles.anim')


def prepare(project, release):
    if project.exists():
        raise SystemExit(f'project already exists: {project}')
    base = REPO / 'tests/projects/lifecycle'
    for part in ('ProjectSettings', 'Packages'):
        shutil.copytree(base / part, project / part)
    shutil.copytree(PROBE / 'Assets', project / 'Assets')
    shutil.copytree(base / 'Assets/Scenes', project / 'Assets/Scenes')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + release + '\n')
    dependencies = {f'com.unity.modules.{name}': '1.0.0' for name in ('animation', 'physics')}
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}))
    (project / 'Assets/Resources').mkdir(exist_ok=True)
    for name in SHARED:
        for suffix in ('', '.meta'):
            shutil.copy2(REPO / 'tests/projects/animculling/Assets/Resources' / (name + suffix),
                         project / 'Assets/Resources' / (name + suffix))


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
    records = [json.loads(line) for line in values.read_text().splitlines()]
    if not records or records[-1] != {'complete': True}:
        return 'incomplete record'
    begins = [r['case'] for r in records if r.get('event') == 'case.begin']
    ends = [r['case'] for r in records if r.get('event') == 'case.end']
    if begins != ends or not begins or len(set(begins)) != len(begins):
        return 'missing, duplicate or unordered cases'
    if any(r.get('event') == 'error' for r in records):
        return 'the probe logged an error'
    return None


def compare(reference, actual):
    """Strict: every record, in order. Prints every missing, extra or different line."""
    left, right = reference.read_text().splitlines(), actual.read_text().splitlines()
    matches = sum(a == b for a, b in zip(left, right))
    if left == right:
        print(f'{len(left)}/{len(left)} records match')
        return 0
    print('\n'.join(difflib.unified_diff(left, right, fromfile=str(reference), tofile=str(actual), lineterm='')))
    print(f'{matches}/{max(len(left), len(right))} records match')
    return 1


def job(project, out, create, release):
    return EditorJob(project, out / 'editor.log', out,
                     args=['-executeMethod', 'ProbeRunner.Create', '-quit'] if create else [],
                     batch=create, graphics=True, timeout=900, label='animcallbacks-' + out.name,
                     env={} if create else {'ANIM_CALLBACKS_OUT': str(out / 'values.jsonl')},
                     launch_inputs={'release': release, 'paths': {
                         'assets': str(project / 'Assets'), 'settings': str(project / 'ProjectSettings'),
                         'packages': str(project / 'Packages'), 'runner': str(PROBE / 'run.py'),
                         'supervisor': str(REPO / 'tests/editor_runner.py')}})


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound'))
    p.add_argument('--release', required=True)
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    args = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    reference = PROBE / 'Reference' / ('unity-' + args.release + '.jsonl')
    if args.mode == 'create':
        project, out = work / 'projects/create', work / 'create'
        prepare(project, args.release)
        result = run(job(project, out, True, args.release))
        print(summary(result))
        if result.get('exit_code') != 0 or result.get('forced_exit'):
            return 1
        for path in sorted((project / 'Assets/Resources').iterdir()):
            if path.name.split('.')[0] in ('GenericEvents', 'HumanEvents', 'LegacyEvents', 'GenericOrder', 'HumanOrder'):
                shutil.copy2(path, PROBE / 'Assets/Resources' / path.name)
                print('wrote', (PROBE / 'Assets/Resources' / path.name).relative_to(REPO))
        return 0
    if args.mode == 'editor':
        jobs = []
        for repeat in REPEATS:
            project, out = work / 'projects' / repeat, work / repeat
            prepare(project, args.release)
            jobs.append(job(project, out, False, args.release))
        for result in run_parallel(jobs):
            print(summary(result))
        problems = [f'{r}: {capture_problem(work / r)}' for r in REPEATS if capture_problem(work / r)]
        if problems:
            raise SystemExit('; '.join(problems))
        return compare(work / 'native1/values.jsonl', work / 'native2/values.jsonl')
    if args.mode == 'publish':
        for repeat in REPEATS:
            problem = capture_problem(work / repeat)
            if problem:
                raise SystemExit(f'invalid capture: {repeat}: {problem}')
        first, second = (work / r / 'values.jsonl' for r in REPEATS)
        if first.read_bytes() != second.read_bytes():
            compare(first, second)
            raise SystemExit('the two captures differ')
        reference.parent.mkdir(exist_ok=True)
        with reference.open('xb') as stream:
            stream.write(first.read_bytes())
        return 0
    project = work / 'projects/unbound'
    prepare(project, args.release)
    out = work / 'unbound'
    out.mkdir()
    cli = os.environ.get('UNBOUND', os.environ.get('UNBOUND_CLI', str(REPO / 'target/release/unbound')))
    with (out / 'engine.log').open('w') as log:
        code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                               cli, 'run', '-p', str(project), '--frames', '200', '--size', '320x240', '--no-watch'],
                              env=dict(os.environ, ANIM_CALLBACKS_OUT=str(out / 'values.jsonl')),
                              stdout=log, stderr=subprocess.STDOUT).returncode
    if code:
        print((out / 'engine.log').read_text()[-3000:])
        return code
    return compare(reference, out / 'values.jsonl')


if __name__ == '__main__':
    sys.exit(main())
