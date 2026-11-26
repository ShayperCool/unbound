#!/usr/bin/env python3
"""Which saves write a file whose text would not change (see README.md).

    run.py create                # author Assets/P, the scene and the settings with the Editor
    run.py native --label L      # a capture with the Linux Editor in batch mode (GPU)
    run.py publish L1 L2         # two identical captures -> Reference/
    run.py check                 # Unbound on a fresh copy against the reference

Each capture opens one fresh copy of the project four times, keeping what the opens leave (the
Editor's Library/, Unbound's project directory): `open1` with nothing imported yet, `open2` with
nothing changed (both make the cases), `open3` with nothing changed that does nothing (it plays and
leaves), `open4` that makes the cases, leaves Play mode and exits the Editor in Edit mode. Before each open every file of the project gets one old modification time. The rows: for
every file of the project after the open, whether its time is still that one (`<open>.time.<path>`:
`kept`, `new`, `<removed>`) and its content (`<open>.exit.<path>`, tests/file_writes.py); the
probe's dirtiness lines, the dispatches listing its assets (the imports of the files saved) and the
callbacks after the probe asks the Editor to leave (`<open>.leaving`).
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
import editor_runner  # noqa: E402
from file_writes import PARTS, exit_rows, files  # noqa: E402
from probe_paths import reference_file, run_root  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
VARIABLE = 'UNCHANGEDSAVES_OUT'
OPENS = ('open1', 'open2', 'open3', 'open4')
# The third open changes nothing: it plays and leaves. The fourth makes the cases, leaves Play mode
# and exits the Editor in Edit mode.
NOOP = {'open3'}
LEAVE = {'open4'}
# The time every file holds before an open (2020-09-13): a write gives a later one.
STAMP = 1_600_000_000
CRLF = 'Assets/P/Crlf.mat'


def stamp(project):
    for part in PARTS:
        for path in (project/part).rglob('*'):
            if path.is_file():
                os.utime(path, (STAMP, STAMP))


def time_rows(project, before):
    rows = []
    now = {p.relative_to(project).as_posix(): p for part in PARTS for p in sorted((project/part).rglob('*')) if p.is_file()}
    for path in sorted(before | now.keys()):
        if path not in now:
            value = '<removed>'
        elif path not in before:
            value = 'new'
        else:
            value = 'kept' if now[path].stat().st_mtime == STAMP else 'written'
        rows.append({'key': f'time.{path}', 'value': value})
    return rows


def line_rows(raw):
    """The probe's lines: dirtiness per step and the dispatches that list its assets."""
    rows, dispatches, leaving = [], [], None
    for line in raw.read_text().splitlines() if raw.is_file() else []:
        fields = line.split('\t')
        if fields[0] == 'exit':
            leaving = []
        if leaving is not None and fields[0] in ('appquitting', 'unloading', 'quitting', 'playmode', 'onapplicationquit', 'dispatch'):
            leaving.append(fields[0] + ':' + fields[1] + (f'={fields[3]}' if fields[0] == 'dispatch' and len(fields) > 3 else ''))
        if fields[0] == 'dirty':
            rows.append({'key': f'dirty.{fields[1]}.{fields[2]}', 'value': fields[3]})
        elif fields[0] == 'dispatch' and len(fields) > 3 and fields[3]:
            dispatches.append('/'.join(fields[1:]))
        elif fields[0] == 'exit':
            rows.append({'key': 'exit', 'value': '1'})
    rows.append({'key': 'dispatches', 'value': ' || '.join(dispatches)})
    # The callbacks after the probe asked the Editor to leave, in order.
    rows.append({'key': 'leaving', 'value': ' '.join(leaving or [])})
    return rows


def capture(label, run_open):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    project = out/'project'
    for folder in PARTS:
        shutil.copytree(ROOT/folder, project/folder)
    # A file with CRLF line endings (Git keeps the probe's files as LF).
    crlf = project/CRLF
    crlf.write_bytes(crlf.read_bytes().replace(b'\r\n', b'\n').replace(b'\n', b'\r\n'))
    rows, failed = [], 0
    for name in OPENS:
        stamp(project)
        before = files(project)
        raw = out/f'{name}.txt'
        code = run_open(project, out, name, {VARIABLE: str(raw), 'UNCHANGEDSAVES_NOOP': '1' if name in NOOP else '0',
                                           'UNCHANGEDSAVES_LEAVE': '1' if name in LEAVE else '0'})
        failed |= code != 0
        print(label, name, 'exit', code)
        for row in line_rows(raw) + time_rows(project, before.keys()) + exit_rows(project, before):
            rows.append({'key': f'{name}.{row["key"]}', 'value': row['value']})
    if not failed:
        rows.append({'key': 'complete', 'value': '1'})
    (out/OUT).write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in rows))
    return int(failed)


def editor_job(project, out, args, env, label):
    out.mkdir(parents=True, exist_ok=True)
    return editor_runner.EditorJob(project, out/'editor.log', out, args, batch=True, graphics=True,
                                   env=env, timeout=1200, label=label)


def native(label):
    def run_open(project, out, name, env):
        result = editor_runner.run(editor_job(project, out/name, ['-executeMethod', 'UnchangedSavesProbe.Record'],
                                              env, f'unchangedsaves-{label}-{name}'))
        print(editor_runner.summary(result))
        return 1 if result.get('exit_code') != 0 or result.get('forced_exit') else 0
    return capture(label, run_open)


def engine(label):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')

    def run_open(project, out, name, env):
        command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '900', cli, 'run', '-p', str(project),
                   '--no-watch', '--headless', '10']
        with (out/f'{name}.engine.log').open('w') as log:
            return subprocess.run(command, cwd=REPO, env=dict(os.environ, **env), stdout=log,
                                  stderr=subprocess.STDOUT).returncode
    return capture(label, run_open)


def create():
    """Authors Assets/P (the materials and the script asset), the scene and the settings as the
    Editor writes them, into the probe."""
    out = WORK/time.strftime('create-%Y%m%d-%H%M%S')
    project = out/'project'
    for folder in PARTS:
        shutil.copytree(ROOT/folder, project/folder)
    result = editor_runner.run(editor_job(project, out/'editor', ['-executeMethod', 'UnchangedSavesProbe.Create', '-quit'],
                                          {}, 'unchangedsaves-create'))
    print(editor_runner.summary(result))
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 1
    for name in ('P', 'Scenes'):
        if (ROOT/'Assets'/name).exists():
            shutil.rmtree(ROOT/'Assets'/name)
        shutil.copytree(project/'Assets'/name, ROOT/'Assets'/name)
        shutil.copyfile(project/'Assets'/f'{name}.meta', ROOT/'Assets'/f'{name}.meta')
    for part in ('ProjectSettings', 'Packages'):
        shutil.rmtree(ROOT/part)
        shutil.copytree(project/part, ROOT/part)
    print('authored Assets/P, Assets/Scenes, ProjectSettings, Packages from', out)
    return 0


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
    for label in labels:
        for name in OPENS:
            problem = editor_runner.result_problem(WORK/label/name)
            if problem:
                sys.exit(f'{label} {name}: {problem}; not publishing')
        rows(WORK/label/OUT)
    captures = [(WORK/label/OUT).read_bytes() for label in labels]
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
        print('FAIL', key, 'expected', json.dumps(expected.get(key))[:400], 'actual', json.dumps(actual.get(key))[:400])
    matched = sum(key in actual and actual[key] == value for key, value in expected.items())
    print(f'unchangedsaves: {matched}/{len(expected)} rows match; {failed} differ; {residual} known residuals')
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['create', 'native', 'engine', 'publish', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    labels = args.labels + ([args.label] if args.label else [])
    if any(Path(label).name != label or label in ('.', '..') for label in labels):
        parser.error('labels must be single directory names')
    if args.mode in ('native', 'engine') and not args.label:
        parser.error('--label is required for native and engine runs')
    if args.mode == 'create':
        return create()
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
