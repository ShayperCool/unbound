#!/usr/bin/env python3
"""Records the unity_IndirectSpecColor witness in the Editor and publishes its references.

    run.py editor --release R --label L    the four Editor captures, ONE AT A TIME: linear/native1,
                                           linear/native2, gamma/native1, gamma/native2; each is
                                           checked by the strict gate (gate.py) before the next is
                                           admitted, and the first rejected capture stops the run
    run.py publish --release R --label L   per profile: both repeats pass the strict gate (exit 0,
                                           no forced stop, NVIDIA, every predeclared key, finite,
                                           no error record) and are byte-identical; then writes
                                           Reference/unity-R/<profile>.jsonl. A profile whose
                                           pair fails is not published (no replacement pair).
    run.py unbound --release R --label L --profile P
                                           one Unbound capture of profile P (UNBOUND_CLI: the
                                           binary; the environment's UNBOUND_MANAGED_DIR selects
                                           the engine), one launcher lease; compare it with
                                           compare.py against Reference/unity-R/P.jsonl

Profiles: `linear` is this project as committed (m_ActiveColorSpace 1); `gamma` is the same
copy with m_ActiveColorSpace 0 and nothing else changed. No bake runs: the scenes' lighting
data is the Editor's own (see README.md). Every capture runs on a fresh copy of this project in
the validation cache; Editors run through tests/editor_runner.py (one installed-launcher lease
each, the GPU on a private display). Do not wrap this driver in with-heavy-job.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root  # noqa: E402
import editor_runner  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
import gate  # noqa: E402

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'indirectspec.jsonl'
PROFILES = ('linear', 'gamma')
REPEATS = ('native1', 'native2')
ORDER = (('linear', 'native1'), ('linear', 'native2'), ('gamma', 'native1'), ('gamma', 'native2'))
# Launched once and rejected (CS1069, zero observations); never reused.
REJECTED_LABELS = ('indirectspec',)
COLOR_SPACE = {'linear': 'm_ActiveColorSpace: 1', 'gamma': 'm_ActiveColorSpace: 0'}
TIMEOUT = 1200    # hard deadline per admitted Editor
# Silence budget, including the cold import before the witness opens its output: the same
# fixture's fresh-copy captures (tests/projects/lightprobes) import in about 10 s and finish in
# about a minute; one Editor runs at a time here.
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


def job(work, profile, repeat, release):
    out = work/profile/repeat
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    project = copy_project(work/'projects'/f'{profile}-{repeat}', profile, release)
    out.mkdir(parents=True)
    inputs = {'release': release, 'paths': {
        'project/Assets': str(project/'Assets'), 'project/Packages': str(project/'Packages'),
        'project/ProjectSettings': str(project/'ProjectSettings'),
        'probe/run.py': str(PROBE/'run.py'), 'probe/gate.py': str(PROBE/'gate.py'),
        'tests/editor_runner.py': str(REPO/'tests/editor_runner.py'),
        'tests/probe_paths.py': str(REPO/'tests/probe_paths.py'),
        'profile': str(REPO/'data/profiles'/('unity-'+'.'.join(release.split('.')[:2])+'.json'))}}
    return editor_runner.EditorJob(
        project, out/'editor.log', out,
        ['-buildTarget', 'Standalone', '-executeMethod', 'ProbeRunner.Record'],
        batch=True, env={'INDIRECT_SPEC_OUT': str(out/OUT)}, timeout=TIMEOUT,
        progress_files=[out/OUT], progress_timeout=PROGRESS,
        label=f'indirectspec-{profile}-{repeat}', launch_inputs=inputs)


def editor(work, release):
    """Sequential admission: linear/native1 first, each launch gated (gate.py) before the next
    one is admitted. Stops at the first failing capture; nothing is relaunched."""
    if work.exists() and any((work/p).exists() for p in PROFILES):
        sys.exit(f'refusing: {work} already holds captures (a label is launched once)')
    for profile, repeat in ORDER:
        result = editor_runner.run(job(work, profile, repeat, release))
        print(editor_runner.summary(result))
        problems = gate.problems(work/profile/repeat, profile, release)
        (work/profile/repeat/'gate.json').write_text(json.dumps({'problems': problems}, indent=1)+'\n')
        if problems:
            print(f'{profile} {repeat}: REJECTED, admitting nothing further')
            for problem in problems:
                print('  ', problem)
            return 1
        print(f'{profile} {repeat}: gate passed')
    return 0


def capture_problem(directory, profile, release):
    """The strict gate (gate.py): every problem, joined."""
    return '; '.join(gate.problems(directory, profile, release))


def publish(work, release):
    target_dir = reference_root(PROBE)
    failed = 0
    for profile in PROFILES:
        dirs = [work/profile/r for r in REPEATS]
        problems = [(r, capture_problem(d, profile, release)) for r, d in zip(REPEATS, dirs)]
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


def unbound(work, profile, release):
    """One engine capture of a profile on a fresh copy (same color-space edit as the Editor's).
    The witness quits by itself after its completion marker; the frame count only bounds it."""
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    out = work/profile/'unbound'
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    project = copy_project(work/'projects'/f'{profile}-unbound', profile, release)
    out.mkdir(parents=True)
    env = dict(os.environ, INDIRECT_SPEC_OUT=str(out/OUT))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--target', 'Standalone', '--no-watch', '--frames', '600', '--size', '320x180'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out/OUT)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--profile', choices=PROFILES, help='unbound mode: the profile to run')
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.label in REJECTED_LABELS:
        parser.error(f'label {args.label} holds rejected captures and is never reused')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.release)
    if args.mode == 'unbound':
        if not args.profile:
            parser.error('unbound mode needs --profile')
        return unbound(work, args.profile, args.release)
    return publish(work, args.release)


if __name__ == '__main__':
    sys.exit(main())
