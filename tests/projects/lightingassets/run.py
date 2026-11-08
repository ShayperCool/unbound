#!/usr/bin/env python3
"""LightingSettings and ShaderVariantCollection probe.

    run.py setup                              # Editor: (re)make the probe's assets and bakes in a copy, copy them back
    run.py record --label native1             # Editor capture (GPU, through the job pool)
    run.py publish native1 native2            # two identical captures become Reference/unity-<version>/
    run.py gate --label gate-1                # engine run compared with Reference/ (the land gate)

The release is $UNBOUND_PROBE_UNITY_VERSION (default 6000.0.71f1). Run output stays under
$XDG_CACHE_HOME/unbound/validation/runs/lightingassets/<label>/.
"""
import argparse
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
import warmup  # noqa: E402

HEAVY = Path.home()/'.cache/compat/with-heavy-job'
IMAGES = ['lit-authored.png', 'lit-edited.png', 'lit-assigned.png', 'realtime-red.png', 'realtime-blue.png',
          'realtimeoff-red.png']
OUT = 'values.jsonl'
END = {'case': 'end'}


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/folder, target/folder)
    (target/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    return target


def work(label):
    return cache_artifact_path(run_root(PROBE, label))


def setup(label):
    """Runs LightingAssetsSetup.Create in a copy, then copies the made assets back."""
    out = work(label)
    project = out/'project'
    copy_project(project)
    for made in ('Settings', 'Scenes'):
        shutil.rmtree(project/'Assets'/made, ignore_errors=True)
        (project/'Assets'/(made+'.meta')).unlink(missing_ok=True)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'LightingAssetsSetup.Create'],
                                  batch=True, graphics=True, timeout=2400, label='lightingassets-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    if result.get('exit_code') != 0:
        return 1
    for made in ('Settings', 'Scenes', 'Shaders', 'Scripts', 'Editor'):
        shutil.rmtree(PROBE/'Assets'/made, ignore_errors=True)
        shutil.copytree(project/'Assets'/made, PROBE/'Assets'/made)
        shutil.copy2(project/'Assets'/(made+'.meta'), PROBE/'Assets'/(made+'.meta'))
    for name in ('GraphicsSettings.asset', 'EditorBuildSettings.asset'):
        shutil.copy2(project/'ProjectSettings'/name, PROBE/'ProjectSettings'/name)
    return 0


def complete(directory):
    problem = editor_runner.jsonl_complete(OUT, END)(directory)
    if problem:
        return problem
    missing = [name for name in IMAGES if not (Path(directory)/name).is_file()]
    return f'missing {missing}' if missing else None


def record(label):
    out = work(label)
    project = copy_project(out/'project')
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'LightingAssetsRunner.Record'],
                                  batch=True, graphics=True, env={'LIGHTING_ASSETS_OUT': str(out)},
                                  timeout=1200, label='lightingassets-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or complete(out)
    if problem:
        print(f'capture {label}: {problem}', file=sys.stderr)
        return 1
    return 0


def publish(labels):
    target = reference_root(PROBE)

    def write(path, first):
        path.mkdir(parents=True)
        for name in [OUT, *IMAGES]:
            shutil.copy2(first/name, path/name)
    return editor_runner.publish([work(label) for label in labels], target, lambda d: [OUT, *IMAGES], write,
                                 complete=[complete], gpu=True)


def gate(label, frames):
    out = work(label)
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    out.mkdir(parents=True)
    project = copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = {**os.environ, 'LIGHTING_ASSETS_OUT': str(out/'engine')}
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(project),
               '--no-watch', '--frames', str(frames), '--size', '320x180']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(f'engine exit {code}; log {out}/engine.log')
    code = subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference_root(PROBE)), str(out/'engine'),
                           '--log', str(out/'engine.log')]).returncode
    for mode in warmup.MODES:
        code |= warmup.gate(sys.modules[__name__], label+'-cold-'+mode, mode)
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['setup', 'record', 'publish', 'gate'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default='current')
    parser.add_argument('--frames', type=int, default=400)
    parser.add_argument('--warmup-mode', choices=('state', *warmup.MODES))
    args = parser.parse_args()
    if args.warmup_mode:
        if args.mode == 'record': return warmup.record(sys.modules[__name__], args.label, args.warmup_mode)
        if args.mode == 'publish': return warmup.publish(sys.modules[__name__], args.labels, args.warmup_mode)
        if args.mode == 'gate' and args.warmup_mode != 'state': return warmup.gate(sys.modules[__name__], args.label, args.warmup_mode)
        parser.error('use record/publish for state mode; setup does not take --warmup-mode')
    if args.mode == 'setup':
        return setup(args.label)
    if args.mode == 'record':
        return record(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    return gate(args.label, args.frames)


if __name__ == '__main__':
    sys.exit(main())
