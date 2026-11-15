#!/usr/bin/env python3
"""The active render texture across frame boundaries and the matrices of command buffers executed
outside cameras, per Editor context, in private project copies.

    run.py editor  --release R --context C --label L   two independent GPU Editor captures
    run.py publish --release R --context C --label L   publishes the identical captures to
                                                       Reference/unity-R/C.jsonl
    run.py unbound --release R --device D [--label L]  runs Unbound on device D (opengl, vulkan)
                                                       and compares with Reference/unity-R/D-window.jsonl

Contexts: opengl-window and vulkan-window (a windowed Editor on the GPU-backed display :0 with
-force-glcore or -force-vulkan, the Game view focused), opengl-batch (a batchmode Editor on a
private display through VirtualGL) and vulkan-batch (a batchmode Editor with -force-vulkan on :0).
Unbound behaves as the windowed Editor (Application.isBatchMode is false), so its runs compare
with the window references; the batch references record what the batchmode Editor does.
Values are compared as modelroot/compare.py does (numbers within 2e-4); residuals.json declares, per
device, the records Unbound is known to differ in, with the reason, and they stay listed in every
report. Run output goes to
$XDG_CACHE_HOME/unbound/validation/runs/framestate/<label>.
"""
import argparse
import importlib.util
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
from editor_runner import EditorJob, run, summary, result_problem, vulkan_display_env  # noqa: E402
from probe_paths import run_root  # noqa: E402

REPEATS = ('native1', 'native2')
CONTEXTS = ('opengl-window', 'opengl-batch', 'vulkan-window', 'vulkan-batch')
COMPARE = PROBE.parent / 'modelroot/compare.py'
RESIDUALS = PROBE / 'residuals.json'
# The Editor captures' Game view (the `screen` record); the end-of-frame matrices follow its aspect.
SIZE = '64x32'


def prepare(project, release):
    if project.exists():
        raise SystemExit(f'project already exists: {project}')
    base = REPO / 'tests/projects/lifecycle'
    for part in ('ProjectSettings', 'Packages'):
        shutil.copytree(base / part, project / part)
    shutil.copytree(PROBE / 'Assets', project / 'Assets')
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
    if not rows or rows[-1] != {'k': 'complete', 'v': 1}:
        return 'incomplete record'
    if any(row['k'] == 'error' for row in rows):
        return 'the probe logged an error'
    if len({row['k'] for row in rows}) != len(rows):
        return 'duplicate keys'
    return None


def job(project, out, release, context):
    device, mode = context.split('-')
    vulkan, batch = device == 'vulkan', mode == 'batch'
    env = {'FRAMESTATE_OUT': str(out / 'values.jsonl')}
    args = ['-executeMethod', 'ProbeRunner.Record'] if batch else []
    if not batch and not vulkan:
        # The windowed OpenGL Editor on the same GPU-backed display as the Vulkan one.
        env = {**vulkan_display_env(), **env}
        args.append('-force-glcore')
    inputs = None if batch else {'release': release, 'paths': {
        'assets': str(project / 'Assets'), 'settings': str(project / 'ProjectSettings'),
        'packages': str(project / 'Packages'), 'runner': str(PROBE / 'run.py'),
        'supervisor': str(REPO / 'tests/editor_runner.py')}}
    return EditorJob(project, out / 'editor.log', out, args=args, batch=batch, graphics=True, vulkan=vulkan,
                     private_display=batch and not vulkan, timeout=900,
                     label='framestate-' + context + '-' + out.name, env=env, launch_inputs=inputs)


def compare(reference, actual):
    return subprocess.run([sys.executable, str(COMPARE), str(reference), str(actual), '--name', 'framestate']).returncode


def compare_engine(reference, actual, device):
    """Every record of the window reference, values as modelroot/compare.py compares them, except
    the records residuals.json declares for the device, which are listed with their reason. A
    declared record that matches fails too (remove it from residuals.json)."""
    spec = importlib.util.spec_from_file_location('modelroot_compare', COMPARE)
    values = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(values)
    want, got = values.load(reference), values.load(actual)
    declared = {key: reason for reason, keys in json.loads(RESIDUALS.read_text())[device].items() for key in keys}
    bad = residual = 0
    for key in sorted(want.keys() | got.keys()):
        a, b = values.fields(want.get(key, {})), values.fields(got.get(key, {}))
        same = key in want and key in got and a.keys() == b.keys() and all(values.same_field(f, a[f], b[f]) for f in a)
        if same and key not in declared:
            continue
        record = {'key': key, 'unity': want.get(key, 'MISSING'), 'unbound': got.get(key, 'MISSING')}
        if key not in declared:
            print('FAIL', json.dumps(record))
            bad += 1
        elif same:
            print('residual resolved (remove it from residuals.json):', key)
            bad += 1
        else:
            print('residual', json.dumps(record))
            residual += 1
    for reason in sorted({declared[key] for key in declared if key in want}):
        print('declared residual reason:', reason)
    print(f'framestate: {len(want) - bad - residual}/{len(want)} records match, {residual} declared residuals')
    return 1 if bad else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    p.add_argument('--release', required=True)
    p.add_argument('--context', choices=CONTEXTS)
    p.add_argument('--device', choices=('opengl', 'vulkan'), default='opengl')
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    args = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.mode != 'unbound' and not args.context:
        p.error('editor and publish need --context')
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    references = PROBE / 'Reference' / ('unity-' + args.release)
    if args.mode == 'editor':
        jobs = []
        for repeat in REPEATS:
            project, out = work / args.context / 'projects' / repeat, work / args.context / repeat
            prepare(project, args.release)
            jobs.append(job(project, out, args.release, args.context))
        # Editors on the shared display :0 run one after the other.
        for result in [run(j) for j in jobs]:
            print(summary(result))
        problems = [f'{r}: {capture_problem(work / args.context / r)}' for r in REPEATS
                    if capture_problem(work / args.context / r)]
        if problems:
            raise SystemExit('; '.join(problems))
        return compare(*(work / args.context / r / 'values.jsonl' for r in REPEATS))
    if args.mode == 'publish':
        for repeat in REPEATS:
            problem = capture_problem(work / args.context / repeat)
            if problem:
                raise SystemExit(f'invalid capture: {repeat}: {problem}')
        first, second = (work / args.context / r / 'values.jsonl' for r in REPEATS)
        if first.read_bytes() != second.read_bytes():
            raise SystemExit('the two captures differ')
        references.mkdir(parents=True, exist_ok=True)
        with (references / (args.context + '.jsonl')).open('xb') as stream:
            stream.write(first.read_bytes())
        return 0
    project = work / 'projects/unbound'
    prepare(project, args.release)
    out = work / 'unbound'
    out.mkdir()
    cli = os.environ.get('UNBOUND', os.environ.get('UNBOUND_CLI', str(REPO / 'target/release/unbound')))
    device = '--force-vulkan' if args.device == 'vulkan' else '--force-glcore'
    with (out / 'engine.log').open('w') as log:
        code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu', 'timeout', '600',
                               cli, 'run', '-p', str(project), device, '--frames', '60', '--size', SIZE, '--no-watch'],
                              env=dict(os.environ, FRAMESTATE_OUT=str(out / 'values.jsonl')),
                              stdout=log, stderr=subprocess.STDOUT).returncode
    if code or not (out / 'values.jsonl').exists():
        print((out / 'engine.log').read_text()[-3000:])
        return code or 1
    return compare_engine(references / (args.device + '-window.jsonl'), out / 'values.jsonl', args.device)


if __name__ == '__main__':
    sys.exit(main())
