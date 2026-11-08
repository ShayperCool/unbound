#!/usr/bin/env python3
"""Windowed Play Mode destroy timing, with private authored project copies."""
import argparse
import difflib
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
from editor_runner import EditorJob, run, summary, result_problem
from probe_paths import run_root


def prepare(project, release):
    if project.exists():
        raise SystemExit(f'project already exists: {project}')
    base = REPO / 'tests/projects/lifecycle'
    for part in ('ProjectSettings', 'Packages'):
        shutil.copytree(base / part, project / part)
    shutil.copytree(PROBE / 'Assets', project / 'Assets')
    shutil.copytree(base / 'Assets/Scenes', project / 'Assets/Scenes')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + release + '\n')
    dependencies = {f'com.unity.modules.{name}': '1.0.0' for name in ('animation', 'audio', 'physics', 'particlesystem', 'ui', 'uielements')}
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}))
    resources = project / 'Assets/Resources'
    resources.mkdir(exist_ok=True)
    for name in ('HumanA.fbx', 'HumanA.fbx.meta', 'HumanAIK.controller', 'HumanAIK.controller.meta'):
        shutil.copy2(REPO / 'tests/projects/humanoid/Assets/Resources' / name, resources / name)


def capture_problem(directory):
    problem = result_problem(directory, gpu=True)
    if problem:
        return problem
    result = json.loads((directory / 'editor-run.json').read_text())
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 'capture did not exit normally'
    records = [json.loads(line) for line in (directory / 'values.jsonl').read_text().splitlines()]
    if not records or records[-1] != {'complete': True}:
        return 'incomplete record'
    begins = [r['case'] for r in records if r.get('event') == 'case.begin']
    ends = [r['case'] for r in records if r.get('event') == 'case.end']
    if begins != ends or len(begins) < 40 or len(set(begins)) != len(begins):
        return 'missing, duplicate or unordered cases'
    if any(r['detail'] != 'issued=true' for r in records if r.get('event') == 'case.end'):
        return 'a case did not issue its request'
    return None


def compare(reference, actual):
    left, right = reference.read_text().splitlines(), actual.read_text().splitlines()
    if left == right:
        print(f'{len(left)}/{len(left)} records match')
        return 0
    print('\n'.join(difflib.unified_diff(left, right, fromfile=str(reference), tofile=str(actual))))
    return 1


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound', 'compare'))
    p.add_argument('--release', required=True)
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    p.add_argument('--batch', action='store_true')
    p.add_argument('--default-loop', action='store_true')
    p.add_argument('--repeat', choices=('native1', 'native2'), default='native1')
    args = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    reference = PROBE / 'Reference' / ('unity-' + args.release + '.jsonl')
    if args.mode in ('editor', 'create'):
        out = work / args.repeat
        project = work / 'projects' / args.repeat
        create = args.mode == 'create'
        if not create and args.repeat == 'native2':
            problem = capture_problem(work / 'native1')
            if problem:
                raise SystemExit('first capture failed: ' + problem)
        prepare(project, args.release)
        out.mkdir(parents=True)
        result = run(EditorJob(project, out / 'editor.log', out,
            args=['-executeMethod', 'ProbeRunner.Create', '-quit'] if create else [],
            batch=create or args.batch, graphics=True, timeout=900, label='destroyphases-' + args.repeat,
            env={} if create else {'DESTROY_PHASES_OUT': str(out / 'values.jsonl'), 'DESTROY_PHASES_DEFAULT_LOOP': '1' if args.default_loop else '0'},
            launch_inputs={'release': args.release, 'paths': {'assets': str(project / 'Assets'), 'settings': str(project / 'ProjectSettings'), 'packages': str(project / 'Packages'), 'runner': str(PROBE / 'run.py'), 'supervisor': str(REPO / 'tests/editor_runner.py')}}))
        print(summary(result))
        if result_problem(out, gpu=True) or result.get('exit_code') != 0 or result.get('forced_exit'):
            return 1
        if create:
            for name in ('Event.anim', 'Event.anim.meta'):
                target = PROBE / 'Assets/Resources' / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(project / 'Assets/Resources' / name, target)
        else:
            problem = capture_problem(out)
            if problem:
                raise SystemExit(problem)
        return 0
    if args.mode == 'publish':
        for repeat in ('native1', 'native2'):
            out = work / repeat
            problem = capture_problem(out)
            if problem:
                raise SystemExit(f'invalid capture: {repeat}: {problem}')
        first, second = (work / r / 'values.jsonl' for r in ('native1', 'native2'))
        if compare(first, second):
            return 1
        reference.parent.mkdir(exist_ok=True)
        with reference.open('xb') as stream:
            stream.write(first.read_bytes())
        return 0
    if args.mode == 'unbound':
        project = work / 'projects/unbound'
        prepare(project, args.release)
        out = work / 'unbound'
        out.mkdir()
        cli = os.environ.get('UNBOUND', os.environ.get('UNBOUND_CLI', str(REPO / 'target/release/unbound')))
        with (out / 'engine.log').open('w') as log:
            code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                cli, 'run', '-p', str(project), '--frames', '800', '--size', '320x240', '--no-watch'],
                env=dict(os.environ, DESTROY_PHASES_OUT=str(out / 'values.jsonl')), stdout=log, stderr=subprocess.STDOUT).returncode
        if code:
            print((out / 'engine.log').read_text()[-3000:])
            return code
    return subprocess.run([sys.executable, str(PROBE / 'compare.py'), str(reference), str(work / 'unbound/values.jsonl')]).returncode


if __name__ == '__main__':
    sys.exit(main())
