#!/usr/bin/env python3
"""Record the command buffer probe in the Unity Editor (native), run Unbound (engine), publish, verify.

    run.py native --label L          two independent GPU Editor captures (L-1, L-2) through the job pool
    run.py publish L-1 L-2           two byte-identical captures become Reference/unity-<version>.jsonl
    run.py engine --label L          an Unbound capture (UNBOUND_CLI, else UNBOUND, else target/release)
    run.py verify --candidate PATH   compare a capture with the reference
    run.py check                     an engine run into a new directory, then verify (the landing gate)

The release is $UNBOUND_PROBE_UNITY_VERSION (else tests/probe_paths.py's default). Run output stays
under $XDG_CACHE_HOME/unbound/validation/runs/cmdbuffers/.
"""
import argparse
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, run_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, target/folder)
    (target/'ProjectSettings/ProjectVersion.txt').write_text(f'm_EditorVersion: {unity_version()}\n')


def native(label):
    jobs = []
    for repeat in (f'{label}-1', f'{label}-2'):
        out = WORK/repeat
        copy_project(out/'project')
        jobs.append(editor_runner.EditorJob(out/'project', out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                                            batch=True, graphics=True, env={'CMDBUFFERS_PROBE_OUT': str(out/OUT)},
                                            timeout=1200, label='cmdbuffers-'+repeat))
    for result in editor_runner.run_parallel(jobs):
        print(editor_runner.summary(result))
    return 0 if all((job.out/OUT).exists() for job in jobs) else 1


def engine(label):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, CMDBUFFERS_PROBE_OUT=str(out/OUT))
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '900', cli, 'run', '-p', str(out/'project'),
               '--no-watch', '--frames', '600', '--size', '64x32']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(label, 'exit', code)
    return 0 if code == 0 and (out/OUT).exists() else 1


def publish(labels):
    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    return editor_runner.publish([WORK/label for label in labels], reference_file(ROOT, 'jsonl'), lambda d: [OUT], write,
                                 complete=[editor_runner.jsonl_complete(OUT)])


def verify(candidate, safe_append_skip=False):
    code = subprocess.run([sys.executable, str(ROOT/'compare.py'), str(reference_file(ROOT, 'jsonl')), str(candidate)]).returncode
    if not code or not safe_append_skip:
        return code
    # The task explicitly requires skipping an Append draw without a counter, rather than
    # reproducing the Editor's unrelated-counter writes. Keep the strict report above intact.
    import compare
    ref, act = compare.rows(reference_file(ROOT, 'jsonl')), compare.rows(candidate)
    missing = object()
    differences = {key for key in ref.keys() | act.keys() if ref.get(key, missing) != act.get(key, missing)}
    warning = 'uses the counter of buffer `_App`, which is not set or not an Append or Counter buffer: the draw is skipped'
    if (differences == {'uav.append.nocounter', 'uav.append.nocounter.log'}
            and act.get('uav.append.nocounter') == [0xAAAA0000] * 48
            and 'uav.append.nocounter.log' not in act
            and warning in (candidate.parent/'engine.log').read_text()):
        print('gate: required safe Append skip verified; both raw differences remain above')
        return 0
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'verify', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    if args.mode in ('native', 'engine') and (not args.label or Path(args.label).name != args.label):
        parser.error('--label must be a single directory name')
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return engine(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    if args.mode == 'check':
        label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
        return engine(label) or verify(WORK/label/OUT, safe_append_skip=True)
    if not args.candidate:
        parser.error('verify needs --candidate')
    return verify(args.candidate)


if __name__ == '__main__':
    sys.exit(main())
