#!/usr/bin/env python3
"""OnPostprocessAllAssets as a project opens: the first open, an open with nothing changed, an
open after changes made outside the Editor (see README.md).

    run.py native --label L      # a capture with the macOS Editor in batch mode (the caller holds
                                 # the machine's heavy-job lock)
    run.py publish L1 L2         # two identical captures -> Reference/
    run.py check                 # Unbound on a fresh copy against the reference

Each capture opens one fresh copy of the probe's project three times, keeping what the opens leave
(the Editor's Library/, Unbound's project directory): `open1` with nothing imported yet, `open2`
with nothing changed, `open3` after this driver's changes (`outside_edits`). Each open plays a
few frames and quits. The rows: the calls of the open's first dispatch (`start`), made in Edit mode,
with its lists; the calls made in Play mode; the dispatches the Editor makes during the session for
the files it saved (`session.N`); the main asset types the first open answers (`types.*`); and,
after each open, every file of the project (`<open>.exit.<path>`, tests/file_writes.py).
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
VARIABLE = 'FIRSTIMPORT_OUT'
OPENS = ('open1', 'open2', 'open3')


def outside_edits(project):
    """Changes made between the second and the third open, as a user's tools make them."""
    m = project/'Assets/M'
    text = (m/'Changed.mat').read_text()
    if text.count('_Smoothness: 0.5\n') != 1:
        raise SystemExit('Changed.mat: expected one _Smoothness of 0.5')
    (m/'Changed.mat').write_text(text.replace('_Smoothness: 0.5\n', '_Smoothness: 0.25\n'))
    # A material saved by an older pipeline, without a .meta.
    shutil.copyfile(ROOT/'Assets/M/LitV9.mat', m/'New.mat')
    for name in ('Gone.mat', 'Gone.mat.meta'):
        (m/name).unlink()
    (project/'Assets/Moved').mkdir()
    for name in ('Mover.mat', 'Mover.mat.meta'):
        (m/name).rename(project/'Assets/Moved'/name)
    touched = m/'Touched.mat'
    stamp = touched.stat().st_mtime + 10
    os.utime(touched, (stamp, stamp))
    with (m/'Labelled.mat.meta').open('a') as meta:
        meta.write('labels:\n- Probe\n')
    tags = project/'ProjectSettings/TagManager.asset'
    text = tags.read_text()
    if text.count('  tags: []\n') != 1:
        raise SystemExit('TagManager.asset: expected an empty tag list')
    tags.write_text(text.replace('  tags: []\n', '  tags:\n  - ProbeTag\n'))


def open_rows(name, raw):
    """The rows of one open from the probe's lines."""
    groups, current, others = [], None, []
    for line in raw.read_text().splitlines() if raw.is_file() else []:
        fields = line.split('\t')
        if fields[0] == 'dispatch':
            current = {'calls': [], 'lists': []}
            groups.append(current)
        elif fields[0] == 'call' and current is not None:
            current['calls'].append(fields[1:])
        elif fields[0] in ('imported.assets', 'imported.settings', 'imported.packages', 'imported.other',
                           'imported.distinct', 'deleted', 'moved') and current is not None:
            # The first callback's lists (every callback of a dispatch gets the same arrays).
            current['lists'].append((fields[0], fields[1] if len(fields) > 1 else ''))
        elif fields[0] == 'counts' and current is not None:
            current['lists'].append(('counts', '/'.join(fields[1:])))
        else:
            others.append(fields)
    rows = []

    def put(key, value):
        rows.append({'key': f'{name}.{key}', 'value': value})

    def calls(group):
        # Class, form, didDomainReload, state and LitV9's version (the lists are rows of their own).
        return ' | '.join('/'.join(call[:4] + call[8:]) for call in group['calls'])
    start = next((g for g in groups if g['calls'] and g['calls'][0][2] == '1' and g['calls'][0][3] == 'edit'), None)
    if start is not None:
        put('start.calls', calls(start))
        for key, value in start['lists']:
            put('start.' + key, value)
    play = [g for g in groups if g['calls'] and g['calls'][0][2] == '1' and g['calls'][0][3] == 'play']
    put('play.calls', ' || '.join(calls(g) for g in play))
    session = [g for g in groups if g is not start and g not in play]
    for i, group in enumerate(session):
        put(f'session.{i}.calls', calls(group))
        for key, value in group['lists']:
            put(f'session.{i}.{key}', value)
    for fields in others:
        if fields[0] == 'type':
            put(f'types.{fields[1]}', '/'.join(fields[2:]))
        elif fields[0] == 'exit':
            put('exit.LitV9.version', fields[1])
        elif fields[0] == 'quitting':
            put('quitting', '1')
    return rows


def capture(label, run_open):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    project = out/'project'
    for folder in PARTS:
        shutil.copytree(ROOT/folder, project/folder)
    rows, failed = [], 0
    for name in OPENS:
        if name == 'open3':
            outside_edits(project)
        before = files(project)
        raw = out/f'{name}.txt'
        code = run_open(project, out, name, dict(os.environ, **{VARIABLE: str(raw)}))
        failed |= code != 0
        print(label, name, 'exit', code)
        rows += open_rows(name, raw)
        rows += [{'key': f'{name}.{row["key"]}', 'value': row['value']} for row in exit_rows(project, before)]
    if not failed:
        rows.append({'key': 'complete', 'value': '1'})
    (out/OUT).write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in rows))
    return int(failed)


def native(label):
    if sys.platform != 'darwin':
        raise SystemExit('firstimport records with the macOS Editor in batch mode')
    editor = Path('/Applications/Unity/Hub/Editor')/os.environ.get('UNBOUND_PROBE_UNITY_VERSION', '6000.0.71f1')/'Unity.app/Contents/MacOS/Unity'

    def run_open(project, out, name, env):
        with (out/f'{name}.out').open('w') as log:
            return subprocess.run(['timeout', '-s', 'KILL', '1500', str(editor), '-batchmode', '-projectPath', str(project),
                                   '-executeMethod', 'FirstImportProbe.Record', '-logFile', str(out/f'{name}.editor.log')],
                                  env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    return capture(label, run_open)


def engine(label):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')

    def run_open(project, out, name, env):
        command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '900', cli, 'run', '-p', str(project),
                   '--no-watch', '--headless', '10']
        with (out/f'{name}.engine.log').open('w') as log:
            return subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    return capture(label, run_open)


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
        print('FAIL', key, 'expected', json.dumps(expected.get(key))[:400], 'actual', json.dumps(actual.get(key))[:400])
    matched = sum(key in actual and actual[key] == value for key, value in expected.items())
    print(f'firstimport: {matched}/{len(expected)} rows match; {failed} differ; {residual} known residuals')
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
