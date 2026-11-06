#!/usr/bin/env python3
"""Record Unity's class table (native), publish it, check `unbound doctor` against it.

    run.py native --label native1         # a private project copy, GPU Editor through the job pool
    run.py publish native1 native2         # two agreeing captures become Reference/unity-<version>.jsonl
    run.py check                           # `unbound doctor --all --json` on this project (the landing gate)

Run output stays under $XDG_CACHE_HOME/unbound/validation/runs/classids/.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, run_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, target/folder)


def native(label):
    out = WORK/label
    project = out/'project'
    copy_project(project)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ClassTable.Record'],
                                  batch=True, graphics=True, env={'CLASS_TABLE_OUT': str(out/OUT)},
                                  timeout=1200, label='classids-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    return 0 if (out/OUT).exists() else 1


def publish(labels):
    target = reference_file(ROOT, 'jsonl')

    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    return editor_runner.publish([WORK/label for label in labels], target, lambda d: [OUT], write,
                                 complete=[editor_runner.jsonl_complete(OUT)])


def check():
    """The doctor reads the release's table: every class it lists as unread in the release is a
    concrete non-editor class of the reference, and the project's own classes are all read."""
    out = WORK/(time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid()))
    out.mkdir(parents=True)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '600', cli, 'doctor', '--all', '--json', '-p', str(ROOT)]
    with (out/'doctor.json').open('w') as stdout, (out/'doctor.log').open('w') as stderr:
        code = subprocess.run(command, stdout=stdout, stderr=stderr).returncode
    if code != 0:
        print(f'doctor exit {code}; see {out}/doctor.log')
        return 1
    report = json.loads((out/'doctor.json').read_text())
    table = {}
    for line in reference_file(ROOT, 'jsonl').read_text().splitlines():
        row = json.loads(line)
        if 'id' in row:
            table[row['id']] = row
    problems = []
    if report.get('release') != unity_version():
        problems.append(f"release {report.get('release')} is not {unity_version()}")
    if not report.get('hasTable'):
        problems.append('the doctor has no class table for the release')
    unread = report.get('unreadInRelease', [])
    for entry in unread:
        row = table.get(entry['classId'])
        if row is None or row['abstract'] or row['editorOnly'] or row['name'] != entry['unityName']:
            problems.append(f"listed as unread but not a concrete runtime class: {entry}")
    for entry in report.get('classes', []):
        if entry['class']['reading'] not in ('Read', 'Loader', 'Ignored'):
            problems.append(f"the probe's own class is not read: {entry['class']}")
    for problem in problems:
        print(problem)
    print(f"{len(unread)} release classes without a reader; {len(problems)} problems")
    return 1 if problems else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'publish', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    return check()


if __name__ == '__main__':
    sys.exit(main())
