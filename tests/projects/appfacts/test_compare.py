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


def capture(root, name, value, run=True, log='written', editor_log=None):
    """A capture laid out as run.py writes it: <root>/<name> and its project <root>/projects/<name>."""
    out, project = root/name, root/'projects'/name
    out.mkdir(parents=True)
    started = time.time()
    if run:
        (out/'run.json').write_text(json.dumps({'project': str(project), 'command': [], 'started': started}))
    path = project/'Library/Unbound/Editor.log'
    if log is not None:
        path.parent.mkdir(parents=True)
        path.write_text('' if log == 'empty' else 'Unbound: a line\n')
        if log == 'stale':
            os.utime(path, (started - 3600, started - 3600))
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
