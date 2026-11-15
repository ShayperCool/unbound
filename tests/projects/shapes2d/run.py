#!/usr/bin/env python3
"""Collider2D.GetShapeBounds and GetShapeHash controls, in private project copies.

    run.py editor  --release R --label L       two independent batch GPU Editor captures
    run.py publish --release R --label L       publishes the identical captures to Reference/unity-R.json
    run.py unbound --release R [--label L]     runs Unbound and compares with Reference/
                                               (query2dparity's comparator: numbers within 1e-3 relative;
                                               RESIDUALS below are listed, not hidden)

Run output goes to $XDG_CACHE_HOME/unbound/validation/runs/shapes2d/<label>.
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
from editor_runner import EditorJob, run, summary, result_problem  # noqa: E402
from probe_paths import run_root  # noqa: E402

REPEATS = ('native1', 'native2')
BASE = REPO / 'tests/projects/physics'
spec = importlib.util.spec_from_file_location('query_compare', REPO / 'tests/projects/query2dparity/compare.py')
comparator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparator)


def prepare(project, release):
    if project.exists():
        raise SystemExit(f'project already exists: {project}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(BASE / part, project / part)
    shutil.copy2(PROBE / 'Sources/ProbeEntry.cs', project / 'Assets/Scripts/ProbeEntry.cs')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + release + '\n')
    manifest = json.loads((BASE / 'Packages/manifest.json').read_text())
    manifest['dependencies']['com.unity.modules.tilemap'] = '1.0.0'
    (project / 'Packages/manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


def capture_problem(directory):
    problem = result_problem(directory, gpu=True)
    if problem:
        return problem
    result = json.loads((directory / 'editor-run.json').read_text())
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 'capture did not exit normally'
    values = directory / 'values.json'
    if not values.exists():
        return 'no record'
    data = json.loads(values.read_text())
    errors = [name for name, scenario in data.items() if 'error' in scenario]
    return f'scenarios with errors: {errors}' if errors else None


def job(project, out):
    return EditorJob(project, out / 'editor.log', out, args=['-executeMethod', 'ProbeRunner.Record'],
                     batch=True, graphics=True, timeout=600, label='shapes2d-' + out.name,
                     env={'PHYSICS_PROBE_OUT': str(out / 'values.json')},
                     progress_files=[out / 'values.json'], progress_timeout=300)


# Known differences (scenario, key) -> reason. Each still prints as a failure; one that passes
# again is stale and fails the run, as does any other difference.
RESIDUALS = {
    ('polygon-body', 'hash'): 'polygon vertices on a rotated body differ from Unity in the last bit (geometryframe2d tolerance)',
    ('composite-polygons', 'composite.hash'): 'composite vertices differ from Unity in the last bit (geometryframe2d tolerance)',
    ('composite-outlines', 'composite.hash'): 'composite vertices differ from Unity in the last bit (geometryframe2d tolerance)',
}


def compare(reference, actual, report, residuals=None):
    residuals = {} if residuals is None else residuals
    result = comparator.compare(json.loads(reference.read_text()), json.loads(actual.read_text()))
    failing = {(item['scenario'], item['key']) for item in result['failures']}
    result['residuals'] = sorted(f'{s}/{k}' for s, k in failing & residuals.keys())
    result['stale_residuals'] = sorted(f'{s}/{k}' for s, k in residuals.keys() - failing)
    report.write_text(json.dumps(result, indent=2) + '\n')
    for item in result['failures']:
        reason = residuals.get((item['scenario'], item['key']))
        print('RESIDUAL' if reason else 'FAIL', item['scenario'], item['key'], 'reference', item['reference'],
              'unbound', item['candidate'], f'({reason})' if reason else '')
    for name in result['stale_residuals']:
        print('STALE RESIDUAL', name)
    unexpected = len(failing - residuals.keys())
    print(f"shapes2d: {result['matched']}/{result['records']} values match "
          f"({result['exact']} exact, {len(result['passing_rounding'])} within tolerance), "
          f"{len(result['residuals'])} residuals, {unexpected} unexpected, {len(result['stale_residuals'])} stale; report {report}")
    return 1 if unexpected or result['stale_residuals'] else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    p.add_argument('--release', required=True)
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    args = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    reference = PROBE / 'Reference' / ('unity-' + args.release + '.json')
    if args.mode == 'editor':
        jobs = []
        for repeat in REPEATS:
            project, out = work / 'projects' / repeat, work / repeat
            prepare(project, args.release)
            jobs.append(job(project, out))
        for result in [run(j) for j in jobs]:
            print(summary(result))
        problems = [f'{r}: {capture_problem(work / r)}' for r in REPEATS if capture_problem(work / r)]
        if problems:
            raise SystemExit('; '.join(problems))
        return compare(work / 'native1/values.json', work / 'native2/values.json', work / 'repeats.json')
    if args.mode == 'publish':
        for repeat in REPEATS:
            problem = capture_problem(work / repeat)
            if problem:
                raise SystemExit(f'invalid capture: {repeat}: {problem}')
        first, second = (work / r / 'values.json' for r in REPEATS)
        if first.read_bytes() != second.read_bytes():
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
        code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '600',
                               cli, 'run', '-p', str(project), '--headless', '30000', '--no-watch'],
                              env=dict(os.environ, PHYSICS_PROBE_OUT=str(out / 'values.json')),
                              stdout=log, stderr=subprocess.STDOUT).returncode
    if code or not (out / 'values.json').exists():
        print((out / 'engine.log').read_text()[-3000:])
        return code or 1
    return compare(reference, out / 'values.json', work / 'comparison.json', RESIDUALS)


if __name__ == '__main__':
    sys.exit(main())
