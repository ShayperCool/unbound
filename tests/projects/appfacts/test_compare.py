#!/usr/bin/env python3
"""Check compare.py --machine's console log check on synthetic captures (no Editor, no engine).

    test_compare.py OUT [--compare PATH]

OUT is a new directory for the synthetic captures (under the validation cache, not /tmp).
--compare tests another copy of compare.py, for example the previous revision, to show what
it got wrong. Each case states whether application.consoleLogPath must be reported; the exit
status is nonzero when any case disagrees.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import sys
import time

OS_FACTS = {'kernel': '1.0.0', 'hostname': 'h', 'machineId': 'm', 'cpuModel': 'c', 'cpuVendor': 'v', 'cpuCount': 1,
            'memTotalKiB': 1024, 'powerSupplies': [], 'gpus': []}
LOG_KEY = 'application.consoleLogPath'


def capture(root, name, value, run=True, log='written', editor_log=None,
            physical_alias=False, reported_path=None, relative_project=False):
    """A capture laid out as run.py writes it: <root>/<name> and its project <root>/projects/<name>."""
    out, project = root/name, root/'projects'/name
    out.mkdir(parents=True)
    if physical_alias:
        physical = root/'physical-projects'/name
        physical.mkdir(parents=True)
        project.parent.mkdir(parents=True, exist_ok=True)
        project.symlink_to(physical, target_is_directory=True)
    started = time.time()
    if run:
        recorded = project.relative_to(root) if relative_project else project
        (out/'run.json').write_text(json.dumps({'project': str(recorded), 'command': [], 'started': started}))
    path = project/'Library/Unbound/Editor.log'
    if log is not None:
        path.parent.mkdir(parents=True)
        path.write_text('' if log == 'empty' else 'Unbound: a line\n')
        if log == 'stale':
            os.utime(path, (started - 3600, started - 3600))
    if reported_path == 'expected physical':
        value = str(path.resolve())
    elif reported_path == 'different fresh':
        other = root/'other-logs'/name/'Editor.log'
        other.parent.mkdir(parents=True)
        other.write_text('a different fresh nonempty log\n')
        value = str(other)
    if editor_log:
        value = str(out/editor_log)
    (out/'machine.jsonl').write_text(json.dumps({'k': LOG_KEY, 'v': value}) + '\n')
    (out/'os.json').write_text(json.dumps(OS_FACTS))
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('out', type=Path)
    parser.add_argument('--compare', type=Path, default=Path(__file__).with_name('compare.py'))
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to reuse {args.out}')
    spec = importlib.util.spec_from_file_location('compare_under_test', args.compare)
    compare = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(compare)
    good = '<project>/Library/Unbound/Editor.log'
    cases = [  # name, capture arguments, editor mode, must report consoleLogPath
        ('written log', dict(value=good), False, False),
        ('missing log', dict(value=good, log=None), False, True),
        ('empty log', dict(value=good, log='empty'), False, True),
        ('log older than the run', dict(value=good, log='stale'), False, True),
        ('other file name', dict(value='<project>/Library/Unbound/Other.log'), False, True),
        ('path outside the project', dict(value='/elsewhere/Library/Unbound/Editor.log'), False, True),
        ('no run.json', dict(value=good, run=False), False, True),
        ('editor -logFile', dict(value=None, editor_log='editor.log'), True, False),
        ('editor other file', dict(value=None, editor_log='other.log'), True, True),
        ('masked logical alias', dict(value=good, physical_alias=True), False, False),
        ('absolute physical alias', dict(value=None, physical_alias=True,
                                         reported_path='expected physical'), False, True),
        ('different fresh file', dict(value=None, physical_alias=True,
                                      reported_path='different fresh'), False, True),
        ('relative reported path', dict(value='Library/Unbound/Editor.log'), False, True),
        ('absolute missing file', dict(value=None, physical_alias=True,
                                       reported_path='expected physical', log=None), False, True),
        ('absolute empty file', dict(value=None, physical_alias=True,
                                     reported_path='expected physical', log='empty'), False, True),
        ('absolute stale file', dict(value=None, physical_alias=True,
                                     reported_path='expected physical', log='stale'), False, True),
        ('absolute unrecorded file', dict(value=None, physical_alias=True,
                                          reported_path='expected physical', run=False), False, True),
        ('relative recorded project', dict(value=good, relative_project=True), False, True),
    ]
    wrong = 0
    for i, (name, kwargs, editor, must_fail) in enumerate(cases):
        out = capture(args.out, f'case{i}', **kwargs)
        rows = [f for f in compare.machine(out, editor=editor) if f['key'] == LOG_KEY]
        ok = bool(rows) == must_fail
        wrong += not ok
        print(json.dumps({'case': name, 'expect_reported': must_fail, 'reported': bool(rows), 'ok': ok,
                          'problem': rows[0].get('problem') if rows else None}))
    print(f'{len(cases) - wrong}/{len(cases)} cases as expected ({args.compare})')
    return int(bool(wrong))


if __name__ == '__main__':
    sys.exit(main())
