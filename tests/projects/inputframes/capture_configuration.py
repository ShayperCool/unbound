#!/usr/bin/env python3
"""Record the supplemental Linux Editor keyboard-configuration cause control."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sys
import time

PROBE = Path(__file__).resolve().parent
SOURCE = PROBE / 'ConfigurationControl'
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run, summary, vulkan_display_env
from probe_paths import run_root
spec = importlib.util.spec_from_file_location('configuration_projects', PROBE.parent / 'editinheritance/run.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
shared.PROBE = SOURCE


def problem(capture):
    invalid = shared.capture_problem(capture)
    if invalid:
        return invalid
    values = {row['k']: row['v'] for row in map(json.loads, (capture / 'values.jsonl').read_text().splitlines())}
    if values.get('keyboard.present') != 'True' or values.get('signal.keymapPresent') != 'True':
        return 'the keyboard or process-local keymap needed by the control was unavailable'
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default=str(time.time_ns()))
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, 'configuration-' + args.label)
    reference = SOURCE / 'Reference' / ('unity-' + args.release + '.jsonl')
    if args.mode == 'editor':
        if sys.platform != 'linux':
            parser.error('this supplemental control requires the Linux Editor and its process-local GDK keymap')
        for repeat in ('native1', 'native2'):
            project, out = work / 'projects' / repeat, work / repeat
            shared.prepare(project, args.release)
            settings = project / 'ProjectSettings/ProjectSettings.asset'
            settings.write_text(settings.read_text().replace('activeInputHandler: 0', 'activeInputHandler: 2'))
            for name in ('manifest.json', 'packages-lock.json'):
                source = PROBE / 'Packages' / name
                if source.exists():
                    shutil.copy2(source, project / 'Packages' / name)
            out.mkdir(parents=True)
            result = run(EditorJob(project, out / 'editor.log', out, ['-force-glcore'],
                private_display=False, batch=False, graphics=True, timeout=300,
                env={**vulkan_display_env(), 'INPUT_CONFIG_OUT': str(out / 'values.jsonl')},
                launch_inputs={'release': args.release, 'paths': {'assets': str(project / 'Assets'),
                    'settings': str(project / 'ProjectSettings'), 'packages': str(project / 'Packages'),
                    'runner': str(Path(__file__).resolve())}}))
            print(summary(result), flush=True)
            invalid = problem(out)
            if invalid:
                raise SystemExit(invalid)
    captures = [work / repeat for repeat in ('native1', 'native2')]
    for capture in captures:
        invalid = problem(capture)
        if invalid:
            raise SystemExit(invalid)
    first, second = [(capture / 'values.jsonl').read_bytes() for capture in captures]
    if first != second:
        raise SystemExit('Editor repeats differ')
    if args.mode == 'publish':
        reference.parent.mkdir(parents=True, exist_ok=True)
        with reference.open('xb') as stream:
            stream.write(first)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
