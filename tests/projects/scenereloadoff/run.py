#!/usr/bin/env python3
"""What entering Play mode does to the open scene's objects under each Enter Play Mode Options
setting (see README.md).

    run.py create                # author Assets/Scenes/Main.unity with the Editor
    run.py native --label L      # a capture with the Linux Editor in batch mode (GPU)
    run.py publish L1 L2         # two identical captures -> Reference/
    run.py check                 # Unbound on fresh copies against the reference
    run.py rerow L...            # rewrite captures' rows from their probe lines

Each capture runs every profile on a fresh copy of the project with
ProjectSettings/EditorSettings.asset's Enter Play Mode Options set: `none` (options on, nothing
disabled), `domain` (domain reload disabled), `scene` (scene reload disabled), `both` (domain and
scene reload disabled). Rows: per profile and object, its callbacks with Edit or Play mode and the
instance's count in order, with the lines of no object among them (domain loads, Play mode state
changes, [InitializeOnEnterPlayMode], runtime initializers, sceneLoaded, activeSceneChanged, scene
processing), until the probe leaves (`<profile>.events.<object>`; the order of different objects'
callbacks is not compared), its callbacks after that (`<profile>.leaving.<object>`), and its
template's state at Play mode's third frame (`<profile>.template.<object>`).
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
import editor_runner  # noqa: E402
import residuals as residual_lists  # noqa: E402
from file_writes import PARTS  # noqa: E402
from probe_paths import reference_file, run_root  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
VARIABLE = 'SCENERELOADOFF_OUT'
SCENE = 'Assets/Scenes/Main.unity'
PROFILES = {'none': 0, 'domain': 1, 'scene': 2, 'both': 3}


def copy(project, options):
    for folder in PARTS:
        shutil.copytree(ROOT/folder, project/folder)
    settings = project/'ProjectSettings/EditorSettings.asset'
    text = settings.read_text()
    if text.count('  m_EnterPlayModeOptionsEnabled: 1\n') != 1 or text.count('  m_EnterPlayModeOptions: 0\n') != 1:
        raise SystemExit('EditorSettings.asset: expected the options on, with nothing disabled')
    settings.write_text(text.replace('  m_EnterPlayModeOptions: 0\n', f'  m_EnterPlayModeOptions: {options}\n'))


OBJECTS = ('Always', 'Plain', 'AlwaysInactive', 'AlwaysDisabled')
# Lines of no object, in every object's row: domain loads, Play mode state changes,
# [InitializeOnEnterPlayMode], runtime initializers, sceneLoaded, activeSceneChanged and scene
# processing.
GLOBAL = ('domain', 'playmode', 'enterPlayMode', 'init', 'sceneLoaded', 'activeScene', 'processed')


def rows(name, raw):
    """The profile's rows from the probe's lines, one row of events per object: its callbacks in
    order with the domain loads and Play mode state changes among them (the order of different
    objects' Awake is not specified: MonoBehaviour.Awake, Scripting API 6000.0). The callbacks after
    the probe leaves come in an object order that changes between runs: each object's are kept in
    their own order, in `<profile>.leaving.<object>`."""
    lines, out = [], []
    leaving = None
    for line in raw.read_text().splitlines() if raw.is_file() else []:
        fields = line.split('\t')
        if fields[0] == 'template':
            out.append({'key': f'{name}.template.{fields[1]}', 'value': fields[2]})
        elif fields[0] in ('record', 'opened'):
            continue
        elif fields[0] == 'exit':
            leaving = []
        elif leaving is not None:
            leaving.append(fields)
        else:
            lines.append(fields)
    for obj in OBJECTS:
        events = ['/'.join(f) if f[0] in GLOBAL else '/'.join(f[1:]) for f in lines
                  if f[0] in GLOBAL or f[0] == obj]
        out.append({'key': f'{name}.events.{obj}', 'value': ' | '.join(events)})
        out.append({'key': f'{name}.leaving.{obj}', 'value': ' | '.join('/'.join(f[1:]) for f in leaving or [] if f[0] == obj)})
    return out


def rerow(label):
    """Rewrites a capture's rows from its probe lines (after a change of rows())."""
    out = WORK/label
    old = read(out/OUT)
    all_rows = []
    for name in PROFILES:
        all_rows += rows(name, out/f'{name}.txt')
    all_rows.append({'key': 'complete', 'value': old['complete']})
    (out/OUT).write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in all_rows))


def capture(label, run_one):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    all_rows, failed = [], 0
    for name, options in PROFILES.items():
        project = out/name/'project'
        copy(project, options)
        raw = out/f'{name}.txt'
        code = run_one(project, out/name, {VARIABLE: str(raw)})
        failed |= code != 0
        print(label, name, 'exit', code)
        all_rows += rows(name, raw)
    if not failed:
        all_rows.append({'key': 'complete', 'value': '1'})
    (out/OUT).write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in all_rows))
    return int(failed)


def editor_job(project, out, args, env, label):
    out.mkdir(parents=True, exist_ok=True)
    return editor_runner.EditorJob(project, out/'editor.log', out, args, batch=True, graphics=True,
                                   env=env, timeout=1200, label=label)


def native(label):
    def run_one(project, out, env):
        result = editor_runner.run(editor_job(project, out/'editor', ['-executeMethod', 'SceneReloadProbe.Record'],
                                              env, f'scenereloadoff-{label}-{out.name}'))
        print(editor_runner.summary(result))
        return 1 if result.get('exit_code') != 0 or result.get('forced_exit') else 0
    return capture(label, run_one)


def engine(label):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')

    def run_one(project, out, env):
        command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '600', cli, 'run', '-p', str(project),
                   '--scene', SCENE, '--no-watch', '--headless', '10']
        with (out/'engine.log').open('w') as log:
            return subprocess.run(command, cwd=REPO, env=dict(os.environ, **env), stdout=log,
                                  stderr=subprocess.STDOUT).returncode
    return capture(label, run_one)


def create():
    """Authors the scene (and the settings the Editor writes) into the probe."""
    out = WORK/time.strftime('create-%Y%m%d-%H%M%S')
    project = out/'project'
    for folder in PARTS:
        shutil.copytree(ROOT/folder, project/folder)
    result = editor_runner.run(editor_job(project, out/'editor', ['-executeMethod', 'SceneReloadProbe.Create', '-quit'],
                                          {}, 'scenereloadoff-create'))
    print(editor_runner.summary(result))
    # The Editor's batch quit after authoring may crash at its shutdown (exit 139) once the scene
    # is written and imported: the files are taken when the scene exists.
    if result.get('forced_exit') or not (project/SCENE).is_file() or not (project/(SCENE + '.meta')).is_file():
        return 1
    if (ROOT/'Assets/Scenes').exists():
        shutil.rmtree(ROOT/'Assets/Scenes')
    shutil.copytree(project/'Assets/Scenes', ROOT/'Assets/Scenes')
    shutil.copyfile(project/'Assets/Scenes.meta', ROOT/'Assets/Scenes.meta')
    for part in ('ProjectSettings', 'Packages'):
        shutil.rmtree(ROOT/part)
        shutil.copytree(project/part, ROOT/part)
    print('authored', SCENE, 'from', out)
    return 0


def read(path):
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
        for name in PROFILES:
            problem = editor_runner.result_problem(WORK/label/name/'editor')
            if problem:
                sys.exit(f'{label} {name}: {problem}; not publishing')
        read(WORK/label/OUT)
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


def compare(candidate):
    """Every row exactly; a differing row passes only as a recorded residual (tests/residuals.py)
    whose `actual` is the value Unbound gives now; every difference is printed with both values."""
    expected, actual = read(reference_file(ROOT, 'jsonl')), read(candidate)
    path = ROOT/'residuals.json'
    gate = residual_lists.Gate(residual_lists.load(path if path.is_file() else None))
    for key in sorted(expected):
        if key not in actual:
            gate.missing(key, 'missing from the run')
        elif actual[key] == expected[key]:
            gate.exact_key(key)
        elif not gate.differs(key, {'actual': actual[key]}):
            pass
    for key in sorted(set(actual) - set(expected)):
        gate.missing(key, f'not in the reference: {json.dumps(actual[key])}')
    gate.finish(expected.keys())
    for key, measured, entry in gate.recorded:
        print('expected', key, json.dumps(expected[key]))
    for key, measured, entry, text in gate.failed:
        if key in expected:
            print('expected', key, json.dumps(expected[key]))
    for line in gate.lines():
        print(line)
    matched = sum(key in actual and actual[key] == value for key, value in expected.items())
    print(f'scenereloadoff: {matched}/{len(expected)} rows match; {len(gate.failed)} differ; {len(gate.recorded)} known residuals')
    return int(not gate.ok)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['create', 'native', 'engine', 'publish', 'compare', 'check', 'rerow'])
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
    if args.mode == 'rerow':
        for label in labels:
            rerow(label)
        return 0
    label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
    return engine(label) or compare(WORK/label/OUT)


if __name__ == '__main__':
    sys.exit(main())
