#!/usr/bin/env python3
"""Automatic rendering and end-of-frame coroutine startup, with private project copies.

    run.py editor  --release R --label L       two independent windowed GPU Editor captures
    run.py publish --release R --label L       publishes the identical captures to Reference/unity-R.jsonl
    run.py unbound --release R [--label L]     runs Unbound and compares with Reference/
                                               (integer counters compared exactly)

--pipeline urp runs the probe under the Universal Render Pipeline (reference
Reference/unity-R.urp.jsonl); the default is the built-in pipeline.

Run output goes to $XDG_CACHE_HOME/unbound/validation/runs/eofstartup/<label>.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run, summary, result_problem, vulkan_display_env  # noqa: E402
from probe_paths import run_root  # noqa: E402

REPEATS = ('native1', 'native2')
COMPARE = PROBE.parent / 'modelroot/compare.py'


# The Universal Render Pipeline variant takes another probe's project settings, packages and
# pipeline assets (a Forward renderer without features), and names this probe's scenes in them.
URP_BASE = REPO / 'tests/projects/stpmotion'
URP_ASSETS = ('Settings', 'Settings.meta', 'DefaultVolumeProfile.asset', 'DefaultVolumeProfile.asset.meta',
              'UniversalRenderPipelineGlobalSettings.asset', 'UniversalRenderPipelineGlobalSettings.asset.meta')
URP_SCENE = '    guid: 9b1c2d3e4f5a46b7a8c9d0e1f2a3b4c5\n'
PROBE_SCENE = '    guid: 918218a655aae758ab18756903d05e42\n'


def prepare(project, release, pipeline='builtin'):
    if project.exists():
        raise SystemExit(f'project already exists: {project}')
    base = URP_BASE if pipeline == 'urp' else REPO / 'tests/projects/lifecycle'
    for part in ('ProjectSettings', 'Packages'):
        shutil.copytree(base / part, project / part)
    shutil.copytree(PROBE / 'Assets', project / 'Assets')
    # Register the additive scene in the copied build settings.
    settings = project / 'ProjectSettings/EditorBuildSettings.asset'
    text = settings.read_text()
    if pipeline == 'urp':
        if text.count(URP_SCENE) != 1:
            raise SystemExit(f'{settings}: the base project names another scene')
        text = text.replace(URP_SCENE, PROBE_SCENE).replace('  m_configObjects: {}', '  m_configObjects:')
        for name in URP_ASSETS:
            source = URP_BASE / 'Assets' / name
            (shutil.copytree if source.is_dir() else shutil.copyfile)(source, project / 'Assets' / name)
    settings.write_text(text.replace('  m_configObjects:',
        '  - enabled: 1\n    path: Assets/Scenes/Extra.unity\n    guid: fa2c7063cfc24e6a905c49b3c0564ecc\n  m_configObjects:' + (' {}' if pipeline == 'urp' else ''), 1))
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + release + '\n')
    if pipeline != 'urp':
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {}}))


def capture_problem(directory):
    problem = result_problem(directory, gpu=True)
    if problem:
        return problem
    result = json.loads((directory / 'editor-run.json').read_text())
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 'capture did not exit normally'
    values = directory / 'values.jsonl'
    if not values.exists():
        return 'no record'
    rows = [json.loads(line) for line in values.read_text().splitlines() if line.strip()]
    if not rows or rows[-1] != {'k': 'complete', 'v': 1}:
        return 'incomplete record'
    if any(row['k'] == 'error' for row in rows):
        return 'the probe logged an error'
    if len({row['k'] for row in rows}) != len(rows):
        return 'duplicate keys'
    return None


def job(project, out, release):
    return EditorJob(project, out / 'editor.log', out, args=['-force-glcore'], private_display=False,
                     batch=False, graphics=True, timeout=900, label='eofstartup-' + out.name,
                     env={**vulkan_display_env(), 'EOF_STARTUP_OUT': str(out / 'values.jsonl')},
                     launch_inputs={'release': release, 'paths': {
                         'assets': str(project / 'Assets'), 'settings': str(project / 'ProjectSettings'),
                         'packages': str(project / 'Packages'), 'runner': str(PROBE / 'run.py'),
                         'supervisor': str(REPO / 'tests/editor_runner.py')}})


def compare(reference, actual):
    return subprocess.run([sys.executable, str(COMPARE), str(reference), str(actual), '--name', 'eofstartup']).returncode


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    p.add_argument('--release', required=True)
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    p.add_argument('--pipeline', choices=('builtin', 'urp'), default='builtin')
    args = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    reference = PROBE / 'Reference' / ('unity-' + args.release + ('.urp' if args.pipeline == 'urp' else '') + '.jsonl')
    if args.mode == 'editor':
        jobs = []
        for repeat in REPEATS:
            project, out = work / 'projects' / repeat, work / repeat
            prepare(project, args.release, args.pipeline)
            jobs.append(job(project, out, args.release))
        for result in [run(job) for job in jobs]:
            print(summary(result))
        problems = [f'{r}: {capture_problem(work / r)}' for r in REPEATS if capture_problem(work / r)]
        if problems:
            raise SystemExit('; '.join(problems))
        return compare(work / 'native1/values.jsonl', work / 'native2/values.jsonl')
    if args.mode == 'publish':
        for repeat in REPEATS:
            problem = capture_problem(work / repeat)
            if problem:
                raise SystemExit(f'invalid capture: {repeat}: {problem}')
        first, second = (work / r / 'values.jsonl' for r in REPEATS)
        if first.read_bytes() != second.read_bytes():
            raise SystemExit('the two captures differ')
        reference.parent.mkdir(exist_ok=True)
        with reference.open('xb') as stream:
            stream.write(first.read_bytes())
        return 0
    project = work / 'projects/unbound'
    prepare(project, args.release, args.pipeline)
    out = work / 'unbound'
    out.mkdir()
    cli = os.environ.get('UNBOUND', os.environ.get('UNBOUND_CLI', str(REPO / 'target/release/unbound')))
    with (out / 'engine.log').open('w') as log:
        code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                               cli, 'run', '-p', str(project), '--frames', '120', '--size', '320x240', '--no-watch'],
                              env=dict(os.environ, EOF_STARTUP_OUT=str(out / 'values.jsonl')),
                              stdout=log, stderr=subprocess.STDOUT).returncode
    if code:
        print((out / 'engine.log').read_text()[-3000:])
        return code
    return compare(reference, out / 'values.jsonl')


if __name__ == '__main__':
    sys.exit(main())
