#!/usr/bin/env python3
"""Records the unity_IndirectSpecColor witness in the Editor and publishes its references.

    run.py editor --release R --label L    the four Editor captures, at once: the linear and the
                                           gamma profile, two independent repeats each
    run.py publish --release R --label L   per profile: both repeats exit 0 with no forced stop,
                                           the NVIDIA renderer, complete (marker last) and
                                           byte-identical; then writes
                                           Reference/unity-R/<profile>.jsonl. A profile whose
                                           pair fails is not published (no replacement pair).

Profiles: `linear` is this project as committed (m_ActiveColorSpace 1); `gamma` is the same
copy with m_ActiveColorSpace 0 and nothing else changed. No bake runs: the scenes' lighting
data is the Editor's own (see README.md). Every capture runs on a fresh copy of this project in
the validation cache; Editors run through tests/editor_runner.py (one installed-launcher lease
each, the GPU on a private display). Do not wrap this driver in with-heavy-job.
"""
import argparse
import os
from pathlib import Path
import shutil
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root  # noqa: E402
import editor_runner  # noqa: E402

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
OUT = 'indirectspec.jsonl'
PROFILES = ('linear', 'gamma')
REPEATS = ('native1', 'native2')
COLOR_SPACE = {'linear': 'm_ActiveColorSpace: 1', 'gamma': 'm_ActiveColorSpace: 0'}
TIMEOUT = 1200    # hard deadline per admitted Editor
# Silence budget, including the cold import before the witness opens its output: the same
# fixture's fresh-copy captures (tests/projects/lightprobes) import in about 10 s and finish in
# about a minute; four Editors import at once here.
PROGRESS = 600


def copy_project(dest, profile, release):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    settings = dest/'ProjectSettings/ProjectSettings.asset'
    text = settings.read_text()
    if text.count(COLOR_SPACE['linear']) != 1:
        sys.exit(f'{settings}: expected exactly one "{COLOR_SPACE["linear"]}"')
    settings.write_text(text.replace(COLOR_SPACE['linear'], COLOR_SPACE[profile]))
    return dest


def editor(work, release):
    jobs = []
    for profile in PROFILES:
        for repeat in REPEATS:
            out = work/profile/repeat
            if out.exists():
                sys.exit(f'refusing to overwrite capture: {out}')
            project = copy_project(work/'projects'/f'{profile}-{repeat}', profile, release)
            out.mkdir(parents=True)
            inputs = {'release': release, 'paths': {
                'project/Assets': str(project/'Assets'), 'project/Packages': str(project/'Packages'),
                'project/ProjectSettings': str(project/'ProjectSettings'),
                'probe/run.py': str(PROBE/'run.py'),
                'tests/editor_runner.py': str(REPO/'tests/editor_runner.py'),
                'tests/probe_paths.py': str(REPO/'tests/probe_paths.py'),
                'profile': str(REPO/'data/profiles'/('unity-'+'.'.join(release.split('.')[:2])+'.json'))}}
            jobs.append(editor_runner.EditorJob(
                project, out/'editor.log', out,
                ['-buildTarget', 'Standalone', '-executeMethod', 'ProbeRunner.Record'],
                batch=True, env={'INDIRECT_SPEC_OUT': str(out/OUT)}, timeout=TIMEOUT,
                progress_files=[out/OUT], progress_timeout=PROGRESS,
                label=f'indirectspec-{profile}-{repeat}', launch_inputs=inputs))
    results = editor_runner.run_parallel(jobs)
    for result in results:
        print(editor_runner.summary(result))
    failed = 0
    for profile in PROFILES:
        for repeat in REPEATS:
            problem = capture_problem(work/profile/repeat)
            failed |= bool(problem)
            print(profile, repeat, problem or 'complete, exit 0, no forced exit, NVIDIA')
    return failed


def capture_problem(directory):
    """Strict: exit 0 without any forced stop, the NVIDIA renderer, a complete witness."""
    result = editor_runner.load_result(directory)
    if result is None:
        return 'no Editor result'
    if result.get('forced_exit'):
        return f"forced exit ({result['forced_exit']}: {result.get('forced_detail')})"
    if not (directory/'prelaunch-inputs.json').exists():
        return 'no launch-bound input record'
    return editor_runner.result_problem(directory, gpu=True) or editor_runner.jsonl_complete(OUT)(directory)


def publish(work):
    target_dir = reference_root(PROBE)
    failed = 0
    for profile in PROFILES:
        dirs = [work/profile/r for r in REPEATS]
        problems = [(r, capture_problem(d)) for r, d in zip(REPEATS, dirs)]
        problems = [f'{r}: {p}' for r, p in problems if p]
        if problems:
            print(f'{profile}: not publishing ({"; ".join(problems)})')
            failed = 1
            continue
        differ = editor_runner.differences(dirs, lambda d: [OUT])
        if differ:
            print(f'{profile}: the repeats differ ({"; ".join(differ)}); not publishing')
            failed = 1
            continue
        target = target_dir/f'{profile}.jsonl'
        target_dir.mkdir(parents=True, exist_ok=True)
        editor_runner.publish(
            dirs, target, lambda d: [OUT],
            lambda t, first: (shutil.copyfile(first/OUT, t), print('published', t)),
            complete=[editor_runner.jsonl_complete(OUT)], gpu=True)
    return failed


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.release)
    return publish(work)


if __name__ == '__main__':
    sys.exit(main())
