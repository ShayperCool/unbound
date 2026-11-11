#!/usr/bin/env python3
"""Precomputed realtime global illumination probe.

    run.py setup                              # Editor: (re)make the scenes and precompute them in a copy, copy them back
    run.py setup-systems                      # Editor: add the RoomSystems scene alone
    run.py record --label native1             # Editor capture (GPU, through the job pool)
    run.py publish native1 native2            # two identical captures become Reference/unity-<version>/
    run.py gate --label gate-1                # engine run compared with Reference/ (the land gate)

The release is $UNBOUND_PROBE_UNITY_VERSION (default 6000.0.71f1). Run output stays under
$XDG_CACHE_HOME/unbound/validation/runs/realtimegi/<label>/.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, reference_root, run_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
END = {'case': 'end'}
MADE = ('Scenes', 'Settings')


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/folder, target/folder)
    (target/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    return target


def work(label):
    return cache_artifact_path(run_root(PROBE, label))


def outputs(directory):
    """The capture's files: values.jsonl, then every atlas, directionality and frame it wrote."""
    directory = Path(directory)
    names = sorted(p.name for p in directory.iterdir() if p.suffix in ('.atlas', '.dir', '.png'))
    return [OUT, *names]


def expected():
    """Atlas and frame names the probe writes (from the committed reference when present)."""
    reference = reference_root(PROBE)
    return outputs(reference)[1:] if reference.is_dir() else []


def complete(directory):
    problem = editor_runner.jsonl_complete(OUT, END)(directory)
    if problem:
        return problem
    missing = [name for name in expected() if not (Path(directory)/name).is_file()]
    return f'missing {missing}' if missing else None


def setup(label):
    """Runs RealtimeGISetup.Create in a copy, then copies the made scenes, settings and data back."""
    out = work(label)
    project = out/'project'
    copy_project(project)
    for made in MADE:
        shutil.rmtree(project/'Assets'/made, ignore_errors=True)
        (project/'Assets'/(made+'.meta')).unlink(missing_ok=True)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'RealtimeGISetup.Create'],
                                  batch=True, graphics=True, timeout=3600, label='realtimegi-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    if result.get('exit_code') != 0:
        return 1
    for made in (*MADE, 'Scripts', 'Editor', 'Shaders'):
        shutil.rmtree(PROBE/'Assets'/made, ignore_errors=True)
        shutil.copytree(project/'Assets'/made, PROBE/'Assets'/made)
        shutil.copy2(project/'Assets'/(made+'.meta'), PROBE/'Assets'/(made+'.meta'))
    for name in ('EditorBuildSettings.asset', 'GraphicsSettings.asset'):
        shutil.copy2(project/'ProjectSettings'/name, PROBE/'ProjectSettings'/name)
    return 0


def setup_systems(label):
    """Runs RealtimeGISetup.CreateSystems in a copy, then copies the RoomSystems scene, its settings
    and the build settings back (the other scenes stay as they are)."""
    out = work(label)
    project = out/'project'
    copy_project(project)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'RealtimeGISetup.CreateSystems'],
                                  batch=True, graphics=True, timeout=3600, label='realtimegi-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    if result.get('exit_code') != 0:
        return 1
    for made in MADE:
        for path in (project/'Assets'/made).iterdir():
            if path.name.startswith('RoomSystems'):
                target = PROBE/'Assets'/made/path.name
                if path.is_dir():
                    shutil.rmtree(target, ignore_errors=True)
                    shutil.copytree(path, target)
                else:
                    shutil.copy2(path, target)
    shutil.copy2(project/'ProjectSettings'/'EditorBuildSettings.asset', PROBE/'ProjectSettings'/'EditorBuildSettings.asset')
    return 0


def record(label):
    out = work(label)
    project = copy_project(out/'project')
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'RealtimeGIRunner.Record'],
                                  batch=True, graphics=True, env={'REALTIME_GI_OUT': str(out)},
                                  timeout=1800, label='realtimegi-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or complete(out)
    if problem:
        print(f'capture {label}: {problem}', file=sys.stderr)
        return 1
    return 0


def publish(labels):
    target = reference_root(PROBE)

    def write(path, source):
        path.mkdir(parents=True)
        for name in outputs(source):
            shutil.copy2(source/name, path/name)
    return editor_runner.publish([work(label) for label in labels], target, outputs, write,
                                 complete=[complete], gpu=True)


def gate(label, frames):
    out = work(label)
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    out.mkdir(parents=True)
    project = copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = {**os.environ, 'REALTIME_GI_OUT': str(out/'engine')}
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '1500', cli, 'run', '-p', str(project),
               '--no-watch', '--frames', str(frames), '--size', '320x180']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(f'engine exit {code}; log {out}/engine.log')
    return subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference_root(PROBE)),
                           str(out/'engine')]).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['setup', 'setup-systems', 'record', 'publish', 'gate'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default='current')
    parser.add_argument('--frames', type=int, default=24000)
    args = parser.parse_args()
    if args.mode == 'setup':
        return setup(args.label)
    if args.mode == 'setup-systems':
        return setup_systems(args.label)
    if args.mode == 'record':
        return record(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    return gate(args.label, args.frames)


if __name__ == '__main__':
    sys.exit(main())
