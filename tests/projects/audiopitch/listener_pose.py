#!/usr/bin/env python3
"""Record and replay listener pose diagnostics without prescribing simultaneous-listener order."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

FIXTURE = Path(__file__).resolve().parent
sys.path.insert(0, str(FIXTURE.parents[1]))
from editor_runner import EditorJob, result_problem, run
from value_probe import assemble, compare, read_values


def compare_values(reference, candidate):
    expected = read_values(reference/'values.jsonl')
    actual = read_values(candidate/'values.jsonl')
    matched, failures = compare(expected, actual, 0)
    for failure in failures:
        print(json.dumps(failure))
    print(f'listener pose diagnostic: {matched}/{len(expected)} values match, {len(failures)} failures')
    return len(failures)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('record', 'engine', 'compare', 'publish'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--work', type=Path)
    parser.add_argument('--runtime', type=Path, help='Immutable matching unbound and managed/ runtime')
    parser.add_argument('--a', type=Path, help='First completed Editor run root')
    parser.add_argument('--b', type=Path, help='Independent repeated Editor run root')
    parser.add_argument('--candidate', type=Path, help='Capture directory to compare')
    args = parser.parse_args()
    project_fixture = FIXTURE/'ListenerPose'
    if (project_fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+args.release:
        parser.error('release does not match the fixture')
    reference = FIXTURE/'Reference'/('unity-'+args.release)/'listener-pose'
    if args.action == 'compare':
        if not args.candidate:
            parser.error('compare requires --candidate')
        return int(bool(compare_values(reference, args.candidate)))
    if args.action == 'publish':
        if not args.a or not args.b:
            parser.error('publish requires --a and --b')
        for root in (args.a, args.b):
            problem = result_problem(root/'editor-run', gpu=True)
            if problem:
                raise SystemExit(problem)
            for part in ('Assets', 'Packages'):
                for source in (project_fixture/part).rglob('*'):
                    if source.is_file():
                        assert source.read_bytes() == (root/'project'/source.relative_to(project_fixture)).read_bytes(), source
        first, second = args.a/'capture', args.b/'capture'
        assert compare_values(first, second) == 0
        assert (first/'values.jsonl').read_bytes() == (second/'values.jsonl').read_bytes()
        reference.mkdir(parents=True, exist_ok=False)
        shutil.copyfile(first/'values.jsonl', reference/'values.jsonl')
        return 0
    if not args.work:
        parser.error('record and engine require a fresh --work directory')
    if args.action == 'record' and not os.environ.get('PULSE_SERVER'):
        parser.error('record requires PULSE_SERVER for an explicitly selected private audio server')
    if args.action == 'engine' and not args.runtime:
        parser.error('engine requires --runtime')
    args.work.mkdir(parents=True, exist_ok=False)
    project = assemble(project_fixture, args.work)
    capture = args.work/'capture'
    if args.action == 'record':
        result = run(EditorJob(project, args.work/'editor.log', args.work/'editor-run',
                     args=['-executeMethod', 'ProbeRunner.Record'], batch=True, graphics=True,
                     env={'LISTENER_POSE_OUT': str(capture)}, timeout=900))
        problem = result_problem(args.work/'editor-run', gpu=True)
        if result['exit_code'] or problem:
            raise SystemExit(problem or result)
        read_values(capture/'values.jsonl')
        return 0
    runtime = args.runtime.resolve()
    environment = dict(os.environ, LISTENER_POSE_OUT=str(capture), LISTENER_POSE_ENGINE='1',
                       UNBOUND_MANAGED_DIR=str(runtime/'managed'), LD_LIBRARY_PATH=str(runtime/'managed'),
                       UNBOUND_AUDIO_DEVICE_CHANNELS='8')
    with (args.work/'engine.log').open('w') as log:
        code = subprocess.call([str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-cpu',
                                'timeout', '900', str(runtime/'unbound'), 'run', '-p', str(project),
                                '--scene', 'Assets/Scenes/Probe.unity', '--headless', '300',
                                '--fixed-fps', '60', '--no-watch'], env=environment,
                               stdout=log, stderr=subprocess.STDOUT)
    if code:
        return code
    return int(bool(compare_values(reference, capture)))


if __name__ == '__main__':
    raise SystemExit(main())
