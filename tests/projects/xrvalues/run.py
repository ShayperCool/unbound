#!/usr/bin/env python3
"""Record independent GPU Editor captures or replay the XR value payload controls."""
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
from probe_paths import reference_file, run_root

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'


def project_at(out):
    project = out/'project'
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, project/folder)
    return project


def native(label):
    out = WORK/label
    project = project_at(out)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                                  batch=True, graphics=True, env={'XR_VALUES_OUT': str(out/OUT)},
                                  progress_files=[out/OUT], progress_timeout=300, timeout=900,
                                  label='xrvalues-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return int(result.get('exit_code') != 0 or result.get('forced_exit') or not (out/OUT).is_file())


def engine(label):
    out = WORK/label
    project = project_at(out)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, XR_VALUES_OUT=str(out/OUT))
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(project),
               '--no-watch', '--frames', '200', '--size', '96x64']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print('engine exit', code, 'log', out/'engine.log')
    return code or int(not (out/OUT).is_file())


def publish(labels):
    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/OUT, target)
    return editor_runner.publish([WORK/label for label in labels], reference_file(ROOT, 'jsonl'),
                                 lambda directory: [OUT], write,
                                 complete=[editor_runner.jsonl_complete(OUT, {'key': 'complete', 'value': '1'})])


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


def compare(candidate):
    expected, actual = rows(reference_file(ROOT, 'jsonl')), rows(candidate)
    failed = 0
    for key in sorted(expected.keys() | actual.keys()):
        if expected.get(key) != actual.get(key) or key not in expected or key not in actual:
            print('FAIL', key, 'expected', repr(expected.get(key)), 'actual', repr(actual.get(key)))
            failed += 1
    matched = sum(key in actual and actual[key] == value for key, value in expected.items())
    print(f'{matched}/{len(expected)} records match; {failed} differ')
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    labels = args.labels + ([args.label] if args.label else [])
    if any(Path(label).name != label or label in ('.', '..') for label in labels):
        parser.error('labels must be single directory names')
    if args.mode in ('native', 'engine') and not args.label:
        parser.error('--label is required for native and engine runs')
    if args.mode == 'compare' and args.candidate is None:
        parser.error('--candidate is required for comparison')
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return engine(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    if args.mode == 'compare':
        return compare(args.candidate)
    label = time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    return engine(label) or compare(WORK/label/OUT)


if __name__ == '__main__':
    sys.exit(main())
