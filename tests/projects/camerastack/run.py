#!/usr/bin/env python3
"""Records the stacked screen camera probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --space S --label L [--dry-run]
                    two independent windowed Editor captures (native1, native2)
    run.py check   --release R --space S --label L   validates the two Editor captures
    run.py publish --release R --space S --label L   checks that the captures agree and writes
                                                     Reference/unity-R/S
    run.py unbound --release R --space S --label L   an Unbound capture (UNBOUND_CLI: the binary;
                                                     the environment's UNBOUND_MANAGED_DIR selects
                                                     the engine)

S is Linear or Gamma. Every capture runs on a fresh copy of the probe's Assets/Packages/
ProjectSettings (Gamma: with the color space switched in the copy) under
$XDG_CACHE_HOME/unbound/validation/runs/camerastack/<label> (one label per color space); nothing
is written here except by publish, which refuses to overwrite an existing reference. Each Editor runs through
tests/editor_runner.py (one launcher lease per repeat, a private Xvfb, the GPU through
VirtualGL, the renderer checked); do not wrap this driver in with-heavy-job.

Publishing requires each capture to be complete (strict exit 0 on the GPU, the probe's last
record `complete: true`, one PNG per recorded case) and the two repeats byte-identical.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root
from editor_runner import EditorJob, load_result, publish as publish_repeats, result_problem, \
    run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
# The probe writes its values once, at the end of Play mode, after about 400 frames.
PROGRESS_TIMEOUT = 600
HARD_TIMEOUT = 1200


def copy_project(dest, space):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    if space == 'Gamma':
        settings = dest/'ProjectSettings/ProjectSettings.asset'
        text = settings.read_text()
        if 'm_ActiveColorSpace: 1' not in text:
            sys.exit('the probe settings are not in Linear color space')
        settings.write_text(text.replace('m_ActiveColorSpace: 1', 'm_ActiveColorSpace: 0'))
    return dest


def fresh_output(directory):
    if directory.exists():
        sys.exit(f'refusing to overwrite capture: {directory}')
    directory.mkdir(parents=True)
    return directory


def job(work, repeat):
    project, out = work/'projects'/repeat, work/repeat
    return EditorJob(project, out/'editor.log', out, env={'STACK_PROBE_OUT': str(out)},
                     timeout=HARD_TIMEOUT, progress_files=[out/'values.jsonl'],
                     progress_timeout=PROGRESS_TIMEOUT, label='camerastack-'+repeat,
                     launch_inputs={'release': os.environ['UNBOUND_PROBE_UNITY_VERSION'],
                                    'paths': {'assets': str(project/'Assets'),
                                              'packages': str(project/'Packages'),
                                              'settings': str(project/'ProjectSettings'),
                                              'runner': str(Path(__file__).resolve()),
                                              'supervisor': str(REPO/'tests/editor_runner.py'),
                                              'profile': str(REPO/'data/profiles/unity-6000.0.json')}})


def editor(work, space, dry_run=False):
    jobs = [job(work, repeat) for repeat in REPEATS]
    if dry_run:
        for j in jobs:
            print(json.dumps({'label': j.label, 'kind': j.kind, 'command': j.command(), 'env': j.env,
                              'timeout': j.timeout}, indent=1))
        return 0
    for j in jobs:
        copy_project(j.project, space)
        fresh_output(j.out)
    results = run_parallel(jobs)
    for result in results:
        print(summary(result))
    return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results))


def images(directory):
    return sorted(p.name for p in directory.glob('*.png'))


def editor_problem(repeat):
    reason = result_problem(repeat, gpu=True)
    if not reason and (load_result(repeat) or {}).get('forced_exit'):
        reason = 'forced exits are not accepted by this probe'
    return reason


def capture_problem(repeat):
    """Why the probe output is incomplete (None when it is complete)."""
    if (repeat/'watchdog.txt').exists() or not (repeat/'values.jsonl').exists():
        return 'the capture did not finish'
    try:
        rows = [json.loads(line) for line in (repeat/'values.jsonl').read_text().splitlines() if line.strip()]
    except json.JSONDecodeError:
        return 'values.jsonl is not complete JSON lines'
    keys = [r.get('k') for r in rows]
    if not rows or rows[-1] != {'k': 'complete', 'v': True} or keys.count('complete') != 1:
        return 'the probe did not record complete: true last'
    if any(k == 'fatal' or k.endswith('.fatal') for k in keys):
        return 'the probe recorded a fatal error'
    if len(set(keys)) != len(keys):
        return 'duplicate keys'
    cases = sorted(k.removesuffix('.size')+'.png' for k in keys if k.endswith('.size'))
    if cases != images(repeat):
        return f'images differ from the recorded cases: {sorted(set(cases) ^ set(images(repeat)))[:5]}'
    return None


def check(work):
    status = 0
    for repeat in (work/r for r in REPEATS):
        reason = editor_problem(repeat) or capture_problem(repeat)
        print(repeat.name, reason or 'complete')
        status |= reason is not None
    return int(status)


def write_reference(target, first):
    target.mkdir(parents=True)
    for name in ['values.jsonl', *images(first)]:
        shutil.copyfile(first/name, target/name)
    print('published', target, len(images(first)), 'images')


def publish(work, space):
    repeats = [work/r for r in REPEATS]
    for repeat in repeats:
        if not repeat.is_dir():
            sys.exit(f'{repeat.name}: no capture; not publishing')
        reason = editor_problem(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    return publish_repeats(repeats, reference_root(PROBE)/space, lambda d: ['values.jsonl', *images(d)],
                           write_reference, complete=[capture_problem], gpu=True)


def unbound(work, space):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound', space)
    out = fresh_output(work/'unbound')
    env = dict(os.environ, STACK_PROBE_OUT=str(out))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '2000', '--size', '160x90'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'check'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--space', required=True, choices=('Linear', 'Gamma'))
    parser.add_argument('--dry-run', action='store_true', help='editor: print the jobs without launching')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.dry_run and args.mode != 'editor':
        parser.error('--dry-run applies to editor only')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.space, args.dry_run)
    if args.mode == 'unbound':
        return unbound(work, args.space)
    if args.mode == 'publish':
        return publish(work, args.space)
    return check(work)


if __name__ == '__main__':
    sys.exit(main())
