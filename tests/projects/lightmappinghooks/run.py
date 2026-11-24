#!/usr/bin/env python3
"""Unity Editor reference probe for Lightmapping.GetLightingDataAssetForScene,
AdditionalBakeDelegate stepping, and Lightmapping.Cancel() (Edit mode and Play mode).

    run.py native --label L   # capture Edit + Play mode on the Unity Editor
    run.py publish L1 L2      # two identical captures -> Reference/unity-<version>/
    run.py engine --label L   # capture Edit + Play mode on Unbound
    run.py compare --label L  # compare an engine run against Reference
    run.py check / gate       # engine + compare
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
sys.dont_write_bytecode = True
import editor_runner  # noqa: E402
from probe_paths import private_label, project_copy, reference_root, run_root  # noqa: E402

WORK = run_root(PROBE, 'lightmappinghooks')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
SCENE = 'Assets/Scenes/EnvNoSky.unity'


def cli():
    return os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')


def run_editor(project, out, method, env, label):
    job = editor_runner.EditorJob(project, out/f'editor-{label}.log', out, ['-executeMethod', method],
                                  batch=True, graphics=True, env=env, timeout=1800,
                                  label='lightmappinghooks-' + label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return int(result.get('exit_code') != 0 or bool(result.get('forced_exit')))


def native(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    edit_dir = out/'edit'
    edit_dir.mkdir(parents=True)
    edit_proj = edit_dir/'project'
    project_copy(PROBE, edit_proj, package_cache=False)
    edit_out = out/'values.jsonl'
    code = run_editor(edit_proj, edit_dir, 'LightmappingHooksProbe.Record',
                      {'LM_HOOKS_OUT': str(edit_out), 'LM_HOOKS_EXIT': '1'}, label + '-edit')
    if code or not edit_out.is_file():
        return code or 1
    play_dir = out/'play'
    play_dir.mkdir(parents=True)
    play_proj = play_dir/'project'
    project_copy(PROBE, play_proj, package_cache=False)
    play_out = out/'play.jsonl'
    code = run_editor(play_proj, play_dir, 'LightmappingHooksProbe.RecordPlay',
                      {'LM_HOOKS_PLAY_OUT': str(play_out)}, label + '-play')
    if code or not play_out.is_file():
        return code or 1
    return 0


def parse_jsonl(path):
    rows = {}
    complete = False
    for line in Path(path).read_text().splitlines():
        if not line.strip():
            continue
        item = json.loads(line)
        if item.get('k') == 'complete':
            complete = bool(item.get('v'))
            continue
        rows[item['k']] = item['v']
    if not complete:
        raise ValueError(f'incomplete capture: {path}')
    return rows


def publish(l1, l2):
    ref = reference_root(PROBE)
    for name in ('values.jsonl', 'play.jsonl'):
        b1 = (WORK/l1/name).read_bytes()
        b2 = (WORK/l2/name).read_bytes()
        parse_jsonl(WORK/l1/name)
        if b1 != b2:
            sys.exit(f'publish: {name} differs between {l1} and {l2}')
    ref.mkdir(parents=True, exist_ok=True)
    for name in ('values.jsonl', 'play.jsonl'):
        (ref/name).write_bytes((WORK/l1/name).read_bytes())
    print(f'published lightmappinghooks to {ref}')
    return 0


def engine(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    edit_proj = out/'project-edit'
    project_copy(PROBE, edit_proj, package_cache=False)
    edit_out = out/'values.jsonl'
    cmd = [cli(), 'test', '-p', str(edit_proj), '--platform', 'EditMode']
    if HEAVY.is_file():
        cmd = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '300'] + cmd
    r1 = subprocess.run(cmd, env=dict(os.environ, LM_HOOKS_OUT=str(edit_out), LM_HOOKS_AUTO='1', LM_HOOKS_EXIT='1'),
                        capture_output=True, text=True)
    (out/'engine-edit.log').write_text(r1.stdout + r1.stderr)
    if r1.returncode != 0 or not edit_out.is_file():
        print(f'lightmappinghooks-{label}: edit exit {r1.returncode}')
        return r1.returncode or 1

    play_proj = out/'project-play'
    project_copy(PROBE, play_proj, package_cache=False)
    play_out = out/'play.jsonl'
    cmd2 = [cli(), 'run', '-p', str(play_proj), '--scene', SCENE, '--no-watch', '--frames', '10', '--size', '96x64']
    if HEAVY.is_file():
        cmd2 = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '300'] + cmd2
    r2 = subprocess.run(cmd2, env=dict(os.environ, LM_HOOKS_PLAY_OUT=str(play_out)), capture_output=True, text=True)
    (out/'engine-play.log').write_text(r2.stdout + r2.stderr)
    if r2.returncode != 0 or not play_out.is_file():
        print(f'lightmappinghooks-{label}: play exit {r2.returncode}')
        return r2.returncode or 1
    return 0


def compare(label):
    ref = reference_root(PROBE)
    expected = {}
    actual = {}
    for name in ('values.jsonl', 'play.jsonl'):
        expected.update(parse_jsonl(ref/name))
        actual.update(parse_jsonl(WORK/label/name))
    failed = 0
    for key in sorted(expected.keys() | actual.keys()):
        if expected.get(key) != actual.get(key):
            print(f'FAIL {key}: expected={expected.get(key)!r} actual={actual.get(key)!r}')
            failed += 1
    matched = sum(actual.get(k) == v for k, v in expected.items())
    print(f'lightmappinghooks: {matched}/{len(expected)} values match, {failed} failures')
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'publish', 'engine', 'compare', 'check', 'gate'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    for name in [args.label, *args.labels]:
        if name and (Path(name).name != name or name in ('.', '..')):
            sys.exit('labels are plain names')
    if args.mode == 'native':
        return native(args.label or private_label('native'))
    if args.mode == 'publish':
        if len(args.labels) != 2:
            sys.exit('publish takes two capture labels')
        return publish(args.labels[0], args.labels[1])
    if args.mode == 'engine':
        return engine(args.label or private_label('engine'))
    if args.mode == 'compare':
        if not args.label:
            sys.exit('compare needs --label')
        return compare(args.label)
    label = args.label or private_label('gate')
    code = engine(label)
    return compare(label) if code == 0 else code


if __name__ == '__main__':
    sys.exit(main())
