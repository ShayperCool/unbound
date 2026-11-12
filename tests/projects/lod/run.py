#!/usr/bin/env python3
"""Records the LOD probe in the Unity Editor.

    run.py editor  --release R --label L [--wait MS] [--hold]   two independent Editor captures (native1, native2)
    run.py publish --release R --label L               checks that the two captures agree, writes Reference/lod.jsonl

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/lod/<label>; nothing is written here except by publish.
Editors run through tests/editor_runner.py (each takes its own launcher lease, on the GPU); do
not wrap this driver in with-heavy-job. --wait sets LOD_PROBE_WAIT_MS, the real time each sample
waits after the camera's last render (the probe's default when omitted), and --hold adds the
probe's time-dependent hold rows (LOD_PROBE_HOLD): captures made with either are experiments
and are not published.

The engine run and the comparison are the suite's recipe (tools/agents/suite.py --only lod).
"""
import argparse
import os
from pathlib import Path
import shutil
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import run_root
from editor_runner import EditorJob, result_problem, run_parallel, summary

PROBE = Path(__file__).resolve().parent
# About 12 600 samples of 2 ms and more each, after the import and compilation.
EDITOR_TIMEOUT = 1800


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def editor(work, wait, hold):
    jobs = []
    for repeat in ('native1', 'native2'):
        project = copy_project(work/'projects'/repeat)
        out = work/repeat
        if out.exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        out.mkdir(parents=True)
        env = {'LOD_PROBE_OUT': str(out)}
        if wait is not None:
            env['LOD_PROBE_WAIT_MS'] = wait
        if hold:
            env['LOD_PROBE_HOLD'] = '1'
        if wait is not None or hold:
            (out/'experiment.txt').write_text(f'wait {wait} hold {hold}\n')
        jobs.append(EditorJob(project, out/'editor.log', out, env=env, timeout=EDITOR_TIMEOUT,
                              args=['-executeMethod', 'ProbeRunner.Record']))
    for result in run_parallel(jobs):
        print(summary(result))
    return int(any(result_problem(job.out) for job in jobs))


def publish(work):
    a, b = work/'native1', work/'native2'
    for repeat in (a, b):
        reason = result_problem(repeat, gpu=True)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
        if (repeat/'experiment.txt').exists():
            sys.exit(f'{repeat.name} is an experiment (another wait, or hold rows); not publishing')
        if not (repeat/'lod.jsonl').exists():
            sys.exit(f'{repeat.name}: the capture did not finish; not publishing')
    if (a/'lod.jsonl').read_bytes() != (b/'lod.jsonl').read_bytes():
        sys.exit('the two Editor captures differ; not publishing')
    shutil.copyfile(a/'lod.jsonl', PROBE/'Reference/lod.jsonl')
    print('published', PROBE/'Reference/lod.jsonl')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    parser.add_argument('--wait', help='LOD_PROBE_WAIT_MS for an experiment (not publishable)')
    parser.add_argument('--hold', action='store_true', help='record the hold rows too (not publishable)')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root('lod', args.label)
    return editor(work, args.wait, args.hold) if args.mode == 'editor' else publish(work)


if __name__ == '__main__':
    sys.exit(main())
