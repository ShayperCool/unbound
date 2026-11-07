#!/usr/bin/env python3
"""Record two GPU Editor captures, publish their identical reference, or run an engine-only gate.

All modes require --release (or UNBOUND_PROBE_UNITY_VERSION). Artifacts stay in the
validation cache. create explicitly authors the serialized prefab and mesh fixture.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_file, run_root, unity_version  # noqa: E402
from editor_runner import EditorJob, load_result, publish as publish_repeats, result_problem, \
    run_parallel, summary  # noqa: E402

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
OUT = 'values.jsonl'


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    return dest


def fresh_output(directory):
    if directory.exists():
        sys.exit(f'refusing to overwrite capture: {directory}')
    directory.mkdir(parents=True)
    return directory


def create(work):
    project = copy_project(work/'create')
    log = work/'create.log'
    code = subprocess.run([str(LAUNCHER), '--kind', 'editor-cpu', 'timeout', '-s', 'KILL', '900', str(editor_path()),
                           '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod',
                           'ProbeRunner.Create', '-quit', '-logFile', str(log)]).returncode
    if code:
        sys.exit(f'create failed ({code}), see {log}')
    for path in sorted((project/'Assets').rglob('*')):
        relative = path.relative_to(project)
        if path.is_file() and (path.suffix == '.meta' or relative.parts[1] in ('Scenes', 'Resources')):
            target = PROBE/relative
            if not target.exists() or target.read_bytes() != path.read_bytes():
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target)
                print('wrote', target.relative_to(REPO))
    for name in ('Packages/packages-lock.json', 'ProjectSettings/EditorBuildSettings.asset'):
        if (project/name).exists():
            shutil.copyfile(project/name, PROBE/name)
    return 0


def job(work, repeat):
    project, out = work/'projects'/repeat, work/repeat
    return EditorJob(project, out/'editor.log', out, env={'SKINNEDBOUNDS_PROBE_OUT': str(out)},
                     timeout=1200, progress_files=[out/OUT], progress_timeout=600,
                     screen='1280x720x24', label='skinnedbounds-'+repeat,
                     launch_inputs={'release': unity_version(),
                                    'paths': {'assets': str(project/'Assets'),
                                              'packages': str(project/'Packages'),
                                              'settings': str(project/'ProjectSettings'),
                                              'runner': str(Path(__file__).resolve()),
                                              'supervisor': str(REPO/'tests/editor_runner.py')}})


def editor(work):
    jobs = [job(work, repeat) for repeat in REPEATS]
    for j in jobs:
        copy_project(j.project)
        fresh_output(j.out)
    results = run_parallel(jobs)
    for result in results:
        print(summary(result))
    return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results))


def capture_problem(repeat):
    """Why the probe output is incomplete (None when it is complete)."""
    if (repeat/'watchdog.txt').exists() or not (repeat/OUT).exists():
        return 'the capture did not finish'
    try:
        rows = [json.loads(line) for line in (repeat/OUT).read_text().splitlines() if line.strip()]
    except json.JSONDecodeError:
        return f'{OUT} is not complete JSON lines'
    keys = [r.get('k') for r in rows]
    if not rows or rows[-1] != {'k': 'complete', 'v': True} or keys.count('complete') != 1:
        return 'the probe did not record complete: true last'
    if 'fatal' in keys:
        return 'the probe recorded a fatal error'
    if len(set(keys)) != len(keys):
        return 'duplicate keys'
    return None


def editor_problem(repeat):
    reason = result_problem(repeat, gpu=True)
    if not reason and (load_result(repeat) or {}).get('forced_exit'):
        reason = 'forced exits are not accepted by this probe'
    return reason


def publish(work):
    repeats = [work/r for r in REPEATS]
    for repeat in repeats:
        if not repeat.is_dir():
            sys.exit(f'{repeat.name}: no capture; not publishing')
        reason = editor_problem(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')

    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/OUT, target)
        print('published', target)
    return publish_repeats(repeats, reference_file(PROBE, 'jsonl'), lambda d: [OUT], write,
                           complete=[capture_problem], gpu=True)


def check(work):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    project = copy_project(work/'project')
    out = fresh_output(work/'unbound')
    env = dict(os.environ, SKINNEDBOUNDS_PROBE_OUT=str(out))
    with (out/'unbound.log').open('w') as log:
        code = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', cli, 'run', '-p', str(project),
                               '--no-watch', '--frames', '400', '--size', '192x108'],
                              env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print('unbound exit', code, out)
    if not (out/OUT).exists():
        print('no capture; see', out/'unbound.log')
        return 1
    compared = subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference_file(PROBE, 'jsonl')),
                               str(out/OUT)]).returncode
    return compared or int(code != 0) or int(capture_problem(out) is not None)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'check'))
    parser.add_argument('--label')
    parser.add_argument('--release')
    args = parser.parse_args()
    if not args.release and not os.environ.get('UNBOUND_PROBE_UNITY_VERSION'):
        parser.error('--release or UNBOUND_PROBE_UNITY_VERSION is required')
    if args.release:
        os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    label = args.label or (time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid()) if args.mode in ('check', 'create') else None)
    if not label or Path(label).name != label or label in ('.', '..'):
        parser.error('--label must be a single directory name')
    work = run_root(PROBE, label)
    return {'create': create, 'editor': editor, 'publish': publish, 'check': check}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
