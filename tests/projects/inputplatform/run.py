#!/usr/bin/env python3
"""Record/publish independent GPU Editor repeats, or compare a windowed engine capture."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from editor_runner import EditorJob, run, run_parallel, summary, publish as publish_repeats, jsonl_complete
from probe_paths import run_root, reference_root
from compare import read, compare, compare_fields
P = Path(__file__).resolve().parent
REPO = P.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def project_copy(target):
    for part in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(P/part, target/part)
    return target


def environment(out):
    return {**os.environ, 'INPUTPLATFORM_OUT': str(out),
            'INPUTPLATFORM_CONTROL': str(P/'clipboard_control.py')}


def editor(work, host, screen, display_only=False, work_area=False):
    jobs = []
    for name in ['native1', 'native2']:
        out = work/name
        out.mkdir(parents=True)
        project = project_copy(work/'projects'/name)
        env = environment(out)
        if display_only:
            env['INPUTPLATFORM_DISPLAY_ONLY'] = '1'
        if work_area:
            env['INPUTPLATFORM_WORK_AREA'] = '1'
        jobs.append(EditorJob(project, out/'editor.log', out, env=env,
            private_display=not host, screen=screen, timeout=900,
            progress_files=[out/'values.jsonl'], progress_timeout=480, label='inputplatform-'+name))
    results = [run(job) for job in jobs] if host else run_parallel(jobs)
    for result in results:
        print(summary(result), flush=True)
    return int(any(result['exit_code'] != 0 or result.get('forced_exit') for result in results))


def publish(work, host, context=None):
    def write(target, first):
        target.mkdir(parents=True)
        shutil.copyfile(first/'values.jsonl', target/'values.jsonl')
    return publish_repeats([work/'native1', work/'native2'], reference_root(P)/(context or ('host' if host else 'xvfb')),
        lambda d: ['values.jsonl'], write, complete=[jsonl_complete('values.jsonl', {'k':'complete','v':True})], gpu=True)


def engine(work, cli, work_area=False):
    project = project_copy(work/'project')
    out = work/'unbound'
    out.mkdir(parents=True)
    command = [str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', 'xvfb-run', '-a',
        '-s', '-screen 0 1280x720x24', 'vglrun', '-d', 'egl', str(Path(cli).resolve()),
        'run', '-p', str(project), '--no-watch', '--size', '640x360', '--frames', '1800']
    env = environment(out)
    env.pop('WAYLAND_DISPLAY', None)
    env.pop('WAYLAND_SOCKET', None)
    env['XDG_SESSION_TYPE'] = 'x11'
    env['WINIT_UNIX_BACKEND'] = 'x11'
    env['INPUTPLATFORM_ENGINE'] = '1'
    if work_area:
        env['INPUTPLATFORM_WORK_AREA'] = '1'
        env['INPUTPLATFORM_DISPLAY_ONLY'] = '1'
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, cwd=REPO)
    if result.returncode:
        print(f'engine exit {result.returncode}, see {out / "unbound.log"}')
        return 1
    try:
        ref = read(reference_root(P)/('workarea' if work_area else 'xvfb')/'values.jsonl')
        actual = read(out/'values.jsonl')
        failures = compare_fields(ref, actual) if work_area else compare(ref, actual)
    except (OSError, ValueError, KeyError) as error:
        print(f'invalid capture: {error}')
        return 1
    for failure in failures:
        print('FAIL', failure)
    print(f'{len(ref) - len(failures)}/{len(ref)} records match; {len(failures)} failures; {out}')
    return int(bool(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['editor', 'publish', 'check'])
    parser.add_argument('--release', required=True)
    parser.add_argument('--label')
    parser.add_argument('--cli', default=os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI'))
    parser.add_argument('--host-display', action='store_true')
    parser.add_argument('--work-area-control', action='store_true')
    parser.add_argument('--display-only', action='store_true')
    parser.add_argument('--context', choices=['host', 'xvfb', 'large', 'control', 'workarea'])
    parser.add_argument('--screen', default='1280x720x24')
    a = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
    if a.command == 'check' and not a.cli:
        parser.error('check needs --cli or UNBOUND')
    work = run_root('inputplatform', a.label or ('check-'+str(time.time_ns())))
    if a.command == 'editor':
        return editor(work, a.host_display, a.screen, a.display_only, a.work_area_control)
    if a.command == 'publish':
        return publish(work, a.host_display, a.context)
    return engine(work, a.cli, a.work_area_control)


if __name__ == '__main__':
    sys.exit(main())
