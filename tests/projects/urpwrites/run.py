#!/usr/bin/env python3
"""Which project files a Play mode session in a URP project writes (see README.md).

    run.py native --label L      # a capture with the macOS Editor in batch mode (the caller holds
                                 # the machine's heavy-job lock)
    run.py publish L1 L2         # two byte-identical captures -> Reference/
    run.py check                 # Unbound on a fresh copy against the reference

Each run works on a fresh copy of the probe's project. The probe's rows say which materials are
dirty as they load and play; after the process ends this driver adds `exit.<path>` for every file
of Assets/, Packages/ and ProjectSettings/: `unchanged`, `<removed>`, or its text with GUIDs written
as the path they name and random file ids as their order.
"""
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
from file_writes import PARTS, exit_rows, files  # noqa: E402
from probe_paths import reference_file, run_root  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
RAW = 'probe.jsonl'
VARIABLE = 'URP_WRITES_OUT'
COMPLETE = {'key': 'complete', 'value': '1'}


def project_at(out):
    project = out/'project'
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    for folder in PARTS:
        shutil.copytree(ROOT/folder, project/folder)
    return project


def finish(out, project, initial):
    """values.jsonl: the probe's rows, then what the run left on disk, then the completion marker."""
    raw = out/RAW
    rows = [json.loads(line) for line in raw.read_text().splitlines() if line.strip()] if raw.is_file() else []
    complete = COMPLETE in rows
    rows = [row for row in rows if row != COMPLETE] + exit_rows(project, initial)
    if complete:
        rows.append(COMPLETE)
    (out/OUT).write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in rows))
    return complete


def native(label):
    if sys.platform != 'darwin':
        raise SystemExit('urpwrites records with the macOS Editor in batch mode')
    out = WORK/label
    project = project_at(out)
    initial = files(project)
    editor = Path('/Applications/Unity/Hub/Editor')/os.environ.get('UNBOUND_PROBE_UNITY_VERSION', '6000.0.71f1')/'Unity.app/Contents/MacOS/Unity'
    env = dict(os.environ, **{VARIABLE: str(out/RAW)})
    with (out/'run.out').open('w') as log:
        code = subprocess.run(['timeout', '-s', 'KILL', '1500', str(editor), '-batchmode', '-projectPath', str(project),
                               '-executeMethod', 'UrpWritesProbe.Record', '-logFile', str(out/'editor.log')],
                              env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    complete = finish(out, project, initial)
    print(label, 'Editor exit', code, 'complete', complete)
    return int(code != 0 or not complete)


def engine(label):
    out = WORK/label
    project = project_at(out)
    initial = files(project)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, **{VARIABLE: str(out/RAW)})
    command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '600', cli, 'run', '-p', str(project),
               '--no-watch', '--headless', '600']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    complete = finish(out, project, initial)
    print('engine exit', code, 'complete', complete, 'log', out/'engine.log')
    return int(code != 0 or not complete)


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
    if len(labels) < 2:
        sys.exit('publishing needs two independent repeats')
    captures = [(WORK/label/OUT).read_bytes() for label in labels]
    for label in labels:
        rows(WORK/label/OUT)
    if any(capture != captures[0] for capture in captures):
        sys.exit('the repeats differ; not publishing')
    target = reference_file(ROOT, 'jsonl')
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(captures[0])
    print('published', target)
    return 0


def residuals():
    path = ROOT/'residuals.json'
    return {key: reason for reason, keys in json.loads(path.read_text()).items() for key in keys} if path.is_file() else {}


def compare(candidate):
    """Every row exactly; rows residuals.json names are printed with their cause and do not fail."""
    expected, actual = rows(reference_file(ROOT, 'jsonl')), rows(candidate)
    known = residuals()
    failed = residual = 0
    for key in sorted(expected.keys() | actual.keys()):
        if key in expected and key in actual and expected[key] == actual[key]:
            if key in known:
                print('residual resolved (remove it from residuals.json):', key)
            continue
        if key in known:
            residual += 1
            print('RESIDUAL', key, '-', known[key])
            continue
        failed += 1
        print('FAIL', key, 'expected', json.dumps(expected.get(key)), 'actual', json.dumps(actual.get(key)))
    matched = sum(key in actual and actual[key] == value for key, value in expected.items())
    print(f'urpwrites: {matched}/{len(expected)} rows match; {failed} differ; {residual} known residuals')
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
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
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return engine(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    if args.mode == 'compare':
        return compare(args.candidate)
    label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
    return engine(label) or compare(WORK/label/OUT)


if __name__ == '__main__':
    sys.exit(main())
