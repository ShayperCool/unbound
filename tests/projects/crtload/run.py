#!/usr/bin/env python3
"""Record or replay CRT load scheduling with a repeated public loop phase."""
import argparse
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run, summary, vulkan_display_env
from probe_paths import run_root
spec = importlib.util.spec_from_file_location('crt_projects', PROBE.parent / 'editinheritance/run.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
shared.PROBE = PROBE


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'check'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default=str(time.time_ns()))
    parser.add_argument('--profile', choices=('both', 'normal', 'late-only'), default='both')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    profiles = ('normal', 'late-only') if args.profile == 'both' else (args.profile,)
    code = 0
    for profile in profiles:
        work = run_root(PROBE, args.label + '-' + profile)
        reference = PROBE / 'Reference' / ('unity-' + args.release) / (profile + '.jsonl')
        if args.mode == 'publish':
            captures = [work / repeat for repeat in ('native1', 'native2')]
            for capture in captures:
                problem = shared.capture_problem(capture)
                if problem:
                    raise SystemExit(problem)
            first, second = [(capture / 'values.jsonl').read_bytes() for capture in captures]
            if first != second:
                raise SystemExit('Editor repeats differ')
            reference.parent.mkdir(parents=True, exist_ok=True)
            with reference.open('xb') as stream:
                stream.write(first)
            continue
        repeats = ('native1', 'native2') if args.mode == 'editor' else ('unbound',)
        for repeat in repeats:
            project, out = work / 'projects' / repeat, work / repeat
            shared.prepare(project, args.release)
            out.mkdir(parents=True)
            overrides = {'CRT_LOAD_OUT': str(out / 'values.jsonl'), 'CRT_LATE_ONLY': '1' if profile == 'late-only' else '0'}
            if args.mode == 'editor':
                result = run(EditorJob(project, out / 'editor.log', out, ['-force-glcore'],
                    private_display=False, batch=False, graphics=True, timeout=300,
                    env={**vulkan_display_env(), **overrides},
                    launch_inputs={'release': args.release, 'paths': {'assets': str(project / 'Assets'),
                        'settings': str(project / 'ProjectSettings'), 'packages': str(project / 'Packages'),
                        'runner': str(Path(__file__).resolve())}}))
                print(summary(result), flush=True)
                problem = shared.capture_problem(out)
                if problem:
                    raise SystemExit(problem)
            else:
                cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
                if not cli:
                    parser.error('set UNBOUND_CLI to an immutable runtime')
                with (out / 'engine.log').open('w') as log:
                    result = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                        'timeout', '240', cli, 'run', '-p', str(project), '--no-watch', '--frames', '120', '--size', '32x32'],
                        env=dict(os.environ, **overrides), stdout=log, stderr=subprocess.STDOUT)
                if result.returncode:
                    return result.returncode
                code |= subprocess.run([sys.executable, str(PROBE.parent / 'editinheritance/compare.py'),
                    str(reference), str(out / 'values.jsonl'), '--name', 'crtload.' + profile]).returncode
        if args.mode == 'editor':
            first, second = [(work / repeat / 'values.jsonl').read_bytes() for repeat in repeats]
            if first != second:
                raise SystemExit('Editor repeats differ')
    return code


if __name__ == '__main__':
    raise SystemExit(main())
