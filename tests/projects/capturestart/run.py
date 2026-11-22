#!/usr/bin/env python3
"""When a capture rate set at a point of Play mode's start first steps the game clock.

One run per point (POINTS): the Editor (`native`) or Unbound (`engine`) plays the scene with
CAPTURE_START_AT naming the point, and the probe records the clocks where the rate is set and in
the first frames. `publish` takes two Editor labels whose captures agree byte for byte;
`check` runs Unbound and compares every point with Reference/unity-<release>/."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
import editor_runner
from probe_paths import reference_root, run_root

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
POINTS = ['subsystem', 'before', 'bootAwake', 'sceneAwake', 'sceneEnable', 'after', 'sceneStart', 'bootStart',
          'update0', 'fixedBefore', 'projectFixed']


# The Time settings of the projectFixed point's project: a fixed timestep of 0.05 s (in the
# setting's ticks) and a time scale of 0.5, in place of the probe's own.
PROJECT_FIXED = {'    m_Count: 2822399\n': '    m_Count: 7056000\n', '  m_TimeScale: 1\n': '  m_TimeScale: 0.5\n'}


def project_at(out, point):
    project = out/'project'
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, project/folder)
    if point == 'projectFixed':
        settings = project/'ProjectSettings'/'TimeManager.asset'
        text = settings.read_text()
        for old, new in PROJECT_FIXED.items():
            if text.count(old) != 1:
                raise SystemExit(f'{settings}: expected one {old!r}')
            text = text.replace(old, new)
        settings.write_text(text)
    return project


def native(label):
    failed = 0
    for point in POINTS:
        out = WORK/label/point
        project = project_at(out, point)
        values = out/'values.jsonl'
        job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                                      batch=True, graphics=True, env={'CAPTURE_START_OUT': str(values), 'CAPTURE_START_AT': point},
                                      progress_files=[values], progress_timeout=300, timeout=900,
                                      label=f'capturestart-{label}-{point}')
        result = editor_runner.run(job)
        print(editor_runner.summary(result))
        failed |= int(result.get('exit_code') != 0 or bool(result.get('forced_exit')) or not values.is_file())
    return failed


def engine(label):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    failed = 0
    for point in POINTS:
        out = WORK/label/point
        project = project_at(out, point)
        values = out/'values.jsonl'
        env = dict(os.environ, CAPTURE_START_OUT=str(values), CAPTURE_START_AT=point)
        command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(project),
                   '--no-watch', '--frames', '200', '--size', '96x64']
        with (out/'engine.log').open('w') as log:
            code = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
        if code or not values.is_file():
            print(f'{point}: engine exit {code}, log {out}/engine.log')
            failed = 1
    return failed


def rows(path):
    result = {}
    for line in path.read_text().splitlines():
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError(f'duplicate key {row["key"]} in {path}')
        result[row['key']] = row['value']
    if result.get('complete') != '1':
        raise ValueError(f'incomplete capture {path}')
    return result


def publish(labels):
    if len(labels) != 2:
        sys.exit('publishing needs two independent Editor labels')
    target = reference_root(ROOT)
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    captures = {}
    for point in POINTS:
        first, second = ((WORK/label/point/'values.jsonl') for label in labels)
        for label in labels:
            problem = editor_runner.result_problem(WORK/label/point, gpu=True)
            if problem:
                sys.exit(f'{label}/{point}: {problem}')
        rows(first)
        if first.read_bytes() != second.read_bytes():
            sys.exit(f'{point}: the repeats differ; not publishing')
        captures[point] = first.read_bytes()
    target.mkdir(parents=True)
    for point, data in captures.items():
        (target/f'{point}.jsonl').write_bytes(data)
    print(f'published {len(captures)} points to {target}')
    return 0


def compare(label):
    reference = reference_root(ROOT)
    matched = total = failed = 0
    for point in POINTS:
        expected = rows(reference/f'{point}.jsonl')
        candidate = WORK/label/point/'values.jsonl'
        actual = rows(candidate) if candidate.is_file() else {}
        for key in sorted(expected.keys() | actual.keys()):
            total += key in expected
            if expected.get(key) == actual.get(key):
                matched += 1
            else:
                print('FAIL', f'{point}/{key}', 'expected', repr(expected.get(key)), 'actual', repr(actual.get(key)))
                failed += 1
    print(f'capturestart: {matched}/{total} values match; {failed} differ')
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    labels = args.labels + ([args.label] if args.label else [])
    if any(Path(label).name != label or label in ('.', '..') for label in labels):
        parser.error('labels must be single directory names')
    if args.mode in ('native', 'engine', 'compare') and len(labels) != 1:
        parser.error('one label')
    if args.mode == 'native':
        return native(labels[0])
    if args.mode == 'engine':
        return engine(labels[0])
    if args.mode == 'publish':
        return publish(labels)
    if args.mode == 'compare':
        return compare(labels[0])
    label = time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    return engine(label) or compare(label)


if __name__ == '__main__':
    sys.exit(main())
