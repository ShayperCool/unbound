#!/usr/bin/env python3
"""Record the supplemental Linux Editor keyboard-configuration cause control."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
SOURCE = PROBE / 'ConfigurationControl'
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run, summary, vulkan_display_env
from private_display import Display, GPU_PREFIX, environment
from probe_paths import run_root
from value_probe import engine_cli
spec = importlib.util.spec_from_file_location('configuration_projects', PROBE.parent / 'editinheritance/run.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
shared.PROBE = SOURCE


def problem(capture, host):
    if host:
        path = capture / 'values.txt'
        if not path.is_file() or not path.read_text().endswith('complete\n'):
            return f'incomplete host keyboard capture: {path}'
        return None
    invalid = shared.capture_problem(capture)
    if invalid:
        return invalid
    values = {row['k']: row['v'] for row in map(json.loads, (capture / 'values.jsonl').read_text().splitlines())}
    if values.get('keyboard.present') != 'True' or values.get('signal.keymapPresent') != 'True':
        return 'the keyboard or process-local keymap needed by the control was unavailable'
    return None


def prepare(project, release):
    shared.prepare(project, release)
    settings = project / 'ProjectSettings/ProjectSettings.asset'
    settings.write_text(settings.read_text().replace('activeInputHandler: 0', 'activeInputHandler: 2'))
    for name in ('manifest.json', 'packages-lock.json'):
        source = PROBE / 'Packages' / name
        if source.exists():
            shutil.copy2(source, project / 'Packages' / name)


def engine(work, release, reference):
    project = work / 'projects' / 'unbound'
    out = work / 'unbound'
    prepare(project, release)
    out.mkdir(parents=True)
    display = Display('1280x720x24')
    try:
        env = environment(dict(os.environ, INPUT_HOST_CONFIG_OUT=str(out / 'values.txt'),
                               INPUT_HOST_CONFIG_UNBOUND='1'), display.name)
        with (out / 'unbound.log').open('x') as log:
            command = [str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                'timeout', '180', *GPU_PREFIX, engine_cli(), 'run', '-p', str(project),
                '--size', '960x540', '--frames', '150', '--force-glcore', '--no-watch']
            with subprocess.Popen(command, env=env, stdout=log, stderr=subprocess.STDOUT) as process:
                # There is no window manager to focus the game window on this display.
                focus = subprocess.run(['timeout', '150', 'xdotool', 'search', '--sync',
                    '--onlyvisible', '--name', '^lifecycle$', 'windowfocus', '%1'],
                    env=env, stdout=log, stderr=subprocess.STDOUT)
                if focus.returncode and process.poll() is None:
                    process.terminate()
                exit_code = process.wait()
    finally:
        display.close()
    invalid = problem(out, host=True)
    if focus.returncode or exit_code or invalid:
        raise SystemExit(f'{invalid or "host keyboard engine run failed"}, exit {exit_code}; see {out / "unbound.log"}')
    actual = (out / 'values.txt').read_text()
    expected = reference.read_text()
    if actual != expected:
        import difflib
        print(''.join(difflib.unified_diff(expected.splitlines(True), actual.splitlines(True),
                                          fromfile=str(reference), tofile=str(out / 'values.txt'))), end='')
        return 1
    print(f'keyboard host configuration matches all {len(actual.splitlines()) - 1} values')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default=str(time.time_ns()))
    parser.add_argument('--host', action='store_true', help='use actual X11 input and keymap changes')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, 'configuration-' + args.label)
    filename = 'values.txt' if args.host else 'values.jsonl'
    variable = 'INPUT_HOST_CONFIG_OUT' if args.host else 'INPUT_CONFIG_OUT'
    suffix = '-host.txt' if args.host else '.jsonl'
    reference = SOURCE / 'Reference' / ('unity-' + args.release + suffix)
    if args.mode == 'unbound':
        if not args.host:
            parser.error('the process-local signal control needs the Editor')
        return engine(work, args.release, reference)
    if args.mode == 'editor':
        if sys.platform != 'linux':
            parser.error('this supplemental control requires the Linux Editor and its process-local GDK keymap')
        for repeat in ('native1', 'native2'):
            project, out = work / 'projects' / repeat, work / repeat
            prepare(project, args.release)
            out.mkdir(parents=True)
            display_env = {} if args.host else vulkan_display_env()
            result = run(EditorJob(project, out / 'editor.log', out, ['-force-glcore'],
                private_display=args.host, batch=False, graphics=True, timeout=300,
                env={**display_env, variable: str(out / filename)},
                launch_inputs={'release': args.release, 'paths': {'assets': str(project / 'Assets'),
                    'settings': str(project / 'ProjectSettings'), 'packages': str(project / 'Packages'),
                    'runner': str(Path(__file__).resolve())}}))
            print(summary(result), flush=True)
            if args.host and (result['exit_code'] != 0 or result['forced_exit']
                              or not result['renderer_ok'] or not result['shutdown_logged']):
                raise SystemExit(f'Editor did not finish normally: {out}')
            invalid = problem(out, args.host)
            if invalid:
                raise SystemExit(invalid)
    captures = [work / repeat for repeat in ('native1', 'native2')]
    for capture in captures:
        invalid = problem(capture, args.host)
        if invalid:
            raise SystemExit(invalid)
    first, second = [(capture / filename).read_bytes() for capture in captures]
    if first != second:
        raise SystemExit('Editor repeats differ')
    if args.mode == 'publish':
        reference.parent.mkdir(parents=True, exist_ok=True)
        with reference.open('xb') as stream:
            stream.write(first)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
