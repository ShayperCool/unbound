#!/usr/bin/env python3
"""What loading URP renderer assets of an older layout does, and what the Editor's exit writes (see
README.md).

    run.py create                # author Resources/R/Current.asset with the Editor, derive the others
    run.py native --label L      # a capture with the Linux Editor in batch mode (GPU)
    run.py publish L1 L2         # two identical captures -> Reference/
    run.py check                 # Unbound on a fresh copy against the reference

Each capture opens one fresh copy of the project twice, keeping what the first open leaves (the
Editor's Library/, Unbound's project directory): `open1` with nothing imported yet, `open2` a later
session. Each plays a few frames.
The rows: the probe's `load` lines (each asset's dirtiness after its load, its XR system data
reference and whether its debug sampling shader is set) and every file of the project after the run
(`exit.<path>`, tests/file_writes.py).
"""
import argparse
import hashlib
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
from file_writes import PARTS, exit_rows, files  # noqa: E402
from probe_paths import reference_file, run_root  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
VARIABLE = 'URPRENDERERWRITES_OUT'
OPENS = ('open1', 'open2')




def line_rows(raw):
    """The probe's lines: each asset's state after its load."""
    rows = []
    for line in raw.read_text().splitlines() if raw.is_file() else []:
        fields = line.split('	')
        if fields[0] == 'load':
            rows.append({'key': f'load.{fields[1]}', 'value': '/'.join(fields[2:])})
        elif fields[0] == 'exit':
            rows.append({'key': 'exit', 'value': '1'})
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
        before = files(project)
        raw = out/f'{name}.txt'
        code = run_open(project, out, name, {VARIABLE: str(raw)})
        failed |= code != 0
        print(label, name, 'exit', code)
        for row in line_rows(raw) + exit_rows(project, before):
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
        result = editor_runner.run(editor_job(project, out/name, ['-executeMethod', 'UrpRendererWritesProbe.Record'],
                                              env, f'urprendererwrites-{label}-{name}'))
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


# The older layouts derived from the asset the Editor writes: (name, edit), each a function of its text.
OLD = 'Assets/Resources/R'


def old_layouts(current):
    """{name: text} of the older-layout copies of Current.asset."""
    line = '    probeVolumeSamplingDebugComputeShader: '
    if current.count('\n  xrSystemData: {fileID: 0}\n') != 1 or current.count(line) != 1:
        raise SystemExit('Current.asset: expected one empty xrSystemData and one sampling debug shader')
    missing = current.replace('\n  xrSystemData: {fileID: 0}\n',
                              '\n  xrSystemData: {fileID: 11400000, guid: 0e5a7e5a7e5a7e5a7e5a7e5a7e5a7e5a, type: 2}\n')
    null_shader = re.sub(r'(?m)^    probeVolumeSamplingDebugComputeShader: .*\n', '', current)
    # As an older pipeline wrote it: no probe volume resources, no sampling shader, the shader
    # resources block it had, a missing XR system data asset, a flow map wrapped at the width,
    # asset version 1.
    layout = re.sub(r'(?m)^  probeVolumeResources:\n(    .*\n)*', '', re.sub(r'(?m)^    probeVolumeSamplingDebugComputeShader: .*\n', '',
                    missing))
    layout = layout.replace('    debugReplacementPS: {fileID: 4800000, guid: cf852408f2e174538bcd9b7fda1c5ae7, type: 3}\n',
                            '    debugReplacementPS: {fileID: 4800000, guid: cf852408f2e174538bcd9b7fda1c5ae7,\n      type: 3}\n')
    layout = layout.replace('  m_AssetVersion: 2\n', '  shaders:\n    blitPS: {fileID: 4800000, guid: c17132b1f77d20942aa75f8429c0f8bc, type: 3}\n  m_AssetVersion: 1\n')
    for name, text in (('missing', missing), ('null', null_shader), ('layout', layout)):
        if text == current:
            raise SystemExit(f'{name}: the edit changed nothing')
    return {'OldMissingRef': missing.replace('m_Name: Current', 'm_Name: OldMissingRef'),
            'OldNullShader': null_shader.replace('m_Name: Current', 'm_Name: OldNullShader'),
            'OldLayout': layout.replace('m_Name: Current', 'm_Name: OldLayout')}


def create():
    """Authors Resources/R/Current.asset as the Editor writes it (with the settings and the package
    lock the Editor writes), and the older layouts from it, into the probe."""
    out = WORK/time.strftime('create-%Y%m%d-%H%M%S')
    project = out/'project'
    for folder in PARTS:
        shutil.copytree(ROOT/folder, project/folder)
    result = editor_runner.run(editor_job(project, out/'editor', ['-executeMethod', 'UrpRendererWritesProbe.Create', '-quit'],
                                          {}, 'urprendererwrites-create'))
    print(editor_runner.summary(result))
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 1
    for name in ('Current.asset', 'Current.asset.meta'):
        shutil.copyfile(project/OLD/name, ROOT/OLD/name)
    for scripts in ('Assets/Resources/S', 'Assets/Resources/P'):
        if (ROOT/scripts).exists():
            shutil.rmtree(ROOT/scripts)
        shutil.copytree(project/scripts, ROOT/scripts)
        shutil.copyfile(project/(scripts + '.meta'), ROOT/(scripts + '.meta'))
    for part in ('ProjectSettings', 'Packages'):
        shutil.rmtree(ROOT/part)
        shutil.copytree(project/part, ROOT/part)
    current = (ROOT/OLD/'Current.asset').read_text()
    meta = (ROOT/OLD/'Current.asset.meta').read_text()
    for name, text in old_layouts(current).items():
        (ROOT/OLD/f'{name}.asset').write_text(text)
        guid = hashlib.md5(f'urprendererwrites/{name}'.encode()).hexdigest()
        (ROOT/OLD/f'{name}.asset.meta').write_text(re.sub(r'(?m)^guid: [0-9a-f]{32}$', f'guid: {guid}', meta))
    print('authored', OLD, 'from', out)
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
    print(f'urprendererwrites: {matched}/{len(expected)} rows match; {failed} differ; {residual} known residuals')
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
