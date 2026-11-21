#!/usr/bin/env python3
"""A script's bake (Lightmapping.Bake) of the room as Play mode is entered, and in Play mode.

    script_bake.py setup              # writes Assets/Scenes/RoomScriptBake.unity from RoomBake
    script_bake.py native --label L   # an Editor capture (macOS: batch mode; Linux: the GPU Editor runner)
    script_bake.py publish L1 L2      # two byte-identical captures -> Reference/unity-<version>/scriptbake
    script_bake.py native|publish|engine --edit ...  # the same for the bake in Edit mode (edit.jsonl)
    script_bake.py check              # Unbound against both references

`RoomScriptBake` is `RoomBake` (no lighting data) without the bake on load. Editor code that runs
as Play mode is entered (`Assets/Editor/ScriptBake.cs`, [InitializeOnLoad]) bakes it; Play mode
records the answer, the events, how many renderers that contribute to GI have a realtime
lightmap, and what Bake and BakeAsync answer, send and log in Play mode
(`Assets/Scripts/ScriptBakeProbe.cs`). The reference was recorded with the macOS Editor in batch
mode (what the API answers does not depend on the OS); the lighting itself is the `--bake`
variant's (run.py). Every record is compared exactly.

`--edit`: a bake in Edit mode. `Assets/Editor/EditBake.cs` opens `RoomScriptBake` in Edit mode and
bakes it twice with `Lightmapping.Bake`, recording the answers, the events and the state around
them (the scene's lighting data, dirty flag, files, renderers' realtime lightmap indices), then
enters Play mode, where `Assets/Scripts/EditBakeProbe.cs` records what is drawn. The Editor runs
it with `-executeMethod EditBake.Record` in batch mode on the GPU (Linux); Unbound with
`unbound test` in Edit mode, where it starts itself. Records under `REPORTED` describe the
lighting data asset the Editor writes into the project, which Unbound does not write (it says
so): they are listed, not gated."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
import editor_runner
from probe_paths import reference_root, run_root

WORK = run_root(PROBE, 'scriptbake')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
SCENE = 'Assets/Scenes/RoomScriptBake.unity'


EDIT = 'edit.jsonl'
# Edit-mode records of the lighting data asset the Editor writes into the project (the asset and
# its .meta beside the scene, the scene naming it, at each event and after), which Unbound does
# not write (it says so), and of the Editor's own log during the bake (on this machine an
# assertion about its denoiser library): listed, not gated.
REPORTED = tuple(f'edit.{bake}.{key}' for bake in ('first', 'second')
                 for key in ('files', 'lightingData', 'eventData', 'log')) + ('play.lightingData',)
# How many times the Editor sends an event in a row during a bake (lightingDataUpdated as its
# work goes on: 22 and 14 times in the two bakes of the reference) is not specified (Scripting
# API: "called when any lighting data changes"): the events are compared by their runs.
RUNS = ('edit.first.events', 'edit.second.events')


def runs(events):
    """The events with each run of one event (and state) as one."""
    out = []
    for event in events.split(', ') if events else []:
        if not out or out[-1] != event:
            out.append(event)
    return ', '.join(out)


def reference(edit=False):
    return reference_root(PROBE)/'scriptbake'/(EDIT if edit else OUT)


def setup():
    source = (PROBE/'Assets/Scenes/RoomBake.unity').read_text()
    scene, count = re.subn(r'(?m)^(  m_BakeOnSceneLoad: )1$', r'\g<1>0', source)
    if count != 1:
        sys.exit('RoomBake.unity does not ask for a bake on load once')
    (PROBE/SCENE).write_text(scene)
    (PROBE/(SCENE + '.meta')).write_text('fileFormatVersion: 2\nguid: 5c0f2a4e8b1d4f6a9e3c7b2d1a0f9e8d\n'
                                          'DefaultImporter:\n  externalObjects: {}\n  userData: \n'
                                          '  assetBundleName: \n  assetBundleVariant: \n')
    return 0


def project_at(out):
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    project = out/'project'
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/folder, project/folder)
    return project


def native(label, edit=False):
    out = WORK/label
    project = project_at(out)
    if edit:
        env = {'REALTIME_GI_EDIT_BAKE_OUT': str(out/EDIT)}
        job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'EditBake.Record'],
                                      batch=True, graphics=True, env=env, progress_files=[out/EDIT],
                                      progress_timeout=900, timeout=1800, label='realtimegi-editbake-'+label)
        result = editor_runner.run(job)
        print(editor_runner.summary(result))
        return int(result.get('exit_code') != 0 or result.get('forced_exit') or not (out/EDIT).is_file())
    env = {'REALTIME_GI_SCRIPT_BAKE_OUT': str(out/OUT)}
    if sys.platform == 'darwin':
        editor = Path('/Applications/Unity/Hub/Editor')/os.environ.get('UNBOUND_PROBE_UNITY_VERSION', '6000.0.71f1')/'Unity.app/Contents/MacOS/Unity'
        with (out/'run.out').open('w') as log:
            code = subprocess.run(['timeout', '-s', 'KILL', '1800', str(editor), '-batchmode', '-projectPath', str(project),
                                   '-executeMethod', 'ScriptBake.Record', '-logFile', str(out/'editor.log')],
                                  env=dict(os.environ, **env), stdout=log, stderr=subprocess.STDOUT).returncode
        print(label, 'Editor exit', code)
        return int(code != 0 or not (out/OUT).is_file())
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ScriptBake.Record'],
                                  batch=True, graphics=True, env=env, progress_files=[out/OUT],
                                  progress_timeout=900, timeout=1800, label='realtimegi-scriptbake-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return int(result.get('exit_code') != 0 or result.get('forced_exit') or not (out/OUT).is_file())


def engine(label, edit=False):
    out = WORK/label
    project = project_at(out)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    if edit:
        env = dict(os.environ, REALTIME_GI_EDIT_BAKE_OUT=str(out/EDIT), REALTIME_GI_EDIT_BAKE_AUTO='1')
        command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '900', cli, 'test', '-p', str(project),
                   '--platform', 'EditMode']
        target = out/EDIT
    else:
        env = dict(os.environ, REALTIME_GI_SCRIPT_BAKE_OUT=str(out/OUT))
        command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '900', cli, 'run', '-p', str(project),
                   '--scene', SCENE, '--no-watch', '--frames', '600', '--size', '160x90']
        target = out/OUT
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print('engine exit', code, 'log', out/'engine.log')
    # (`unbound test` without tests answers its run error code: the records decide.)
    return int(not target.is_file()) if edit else code or int(not target.is_file())


def rows(path):
    result = {}
    for line in Path(path).read_text().splitlines():
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError(f'duplicate key {row["key"]} in {path}')
        result[row['key']] = row['value']
    if result.get('complete') != '1':
        raise ValueError(f'incomplete capture {path}')
    return result


def publish(labels, edit=False):
    if len(labels) < 2:
        sys.exit('publishing needs two independent repeats')
    name = EDIT if edit else OUT
    captures = [(WORK/label/name).read_bytes() for label in labels]
    for label in labels:
        rows(WORK/label/name)
    if any(capture != captures[0] for capture in captures):
        sys.exit('the repeats differ; not publishing')
    target = reference(edit)
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(captures[0])
    return 0


def compare(candidate, edit=False):
    expected, actual = rows(reference(edit)), rows(candidate)
    failed = reported = 0
    for key in sorted(expected.keys() | actual.keys()):
        same = expected.get(key) == actual.get(key)
        if edit and key in RUNS and key in expected and key in actual:
            same = runs(expected[key]) == runs(actual[key])
        if not same or key not in expected or key not in actual:
            listed = edit and key in REPORTED
            print('REPORTED' if listed else 'FAIL', key, 'expected', repr(expected.get(key)), 'actual', repr(actual.get(key)))
            reported += listed
            failed += not listed
    matched = sum(key in actual and (actual[key] == value or (edit and key in RUNS and runs(actual[key]) == runs(value)))
                  for key, value in expected.items())
    name = 'scriptbake-edit' if edit else 'scriptbake'
    print(f'{name}: {matched}/{len(expected)} records match; {failed} differ; {reported} reported (not written by Unbound)')
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['setup', 'native', 'engine', 'publish', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--edit', action='store_true', help='the bake in Edit mode (edit.jsonl)')
    args = parser.parse_args()
    labels = args.labels + ([args.label] if args.label else [])
    if any(Path(label).name != label or label in ('.', '..') for label in labels):
        parser.error('labels must be single directory names')
    if args.mode in ('native', 'engine') and not args.label:
        parser.error('--label is required for native and engine runs')
    if args.mode == 'setup':
        return setup()
    if args.mode == 'native':
        return native(args.label, args.edit)
    if args.mode == 'engine':
        return engine(args.label, args.edit)
    if args.mode == 'publish':
        return publish(args.labels, args.edit)
    if args.mode == 'compare':
        if args.candidate is None:
            parser.error('--candidate is required for comparison')
        return compare(args.candidate, args.edit)
    label = time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    failed = engine(label) or compare(WORK/label/OUT)
    return engine(label+'-edit', True) or compare(WORK/(label+'-edit')/EDIT, True) or failed


if __name__ == '__main__':
    sys.exit(main())
