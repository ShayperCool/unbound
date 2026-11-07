#!/usr/bin/env python3
"""Record the Unity Editor (native), run Unbound (engine), publish and verify.

    run.py native --label native1         # a private project copy, GPU Editor through the job pool
    run.py publish native1 native2         # two agreeing captures become Reference/unity-<version>.jsonl
    run.py engine --label final            # Unbound (UNBOUND_CLI or target/release/unbound)
    run.py verify [--candidate PATH]       # compare against the reference (default: the engine run 'final')
    run.py check                           # engine run into a new directory, then verify (the landing gate)

Run output stays under $XDG_CACHE_HOME/unbound/validation/runs/contactmodify3d/.
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
from probe_paths import reference_file, run_root  # noqa: E402
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
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                                  batch=True, graphics=True, env={'CONTACT_MODIFY_OUT': str(out/OUT)},
                                  timeout=1200, label='contactmodify3d-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    return 0 if (out/OUT).exists() else 1


def engine(label):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    out.mkdir(parents=True)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, CONTACT_MODIFY_OUT=str(out/OUT))
    command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '900', cli, 'run', '-p', str(ROOT), '--headless', '30000', '--no-watch']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(label, 'exit', code)
    return 0 if code == 0 and (out/OUT).exists() else 1


def publish(labels, extend=False):
    target = reference_file(ROOT, 'jsonl')

    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    agreed = WORK/('publish-' + str(time.time_ns()))/'agreed.jsonl' if extend else target
    result = editor_runner.publish([WORK/label for label in labels], agreed, lambda d: [OUT], write,
                                   complete=[editor_runner.jsonl_complete(OUT, {'key': 'complete', 'value': [1]})])
    if result or not extend:
        return result
    old_lines = target.read_bytes().splitlines(keepends=True)
    new_lines = agreed.read_bytes().splitlines(keepends=True)
    old = {json.loads(line)['key']: line for line in old_lines}
    new = {json.loads(line)['key']: line for line in new_lines}
    if len(old) != len(old_lines) or len(new) != len(new_lines):
        raise SystemExit('duplicate keys in reference extension')
    if any(new.get(key) != line for key, line in old.items()):
        raise SystemExit('extension would change an existing reference record')
    additions = [line for key, line in new.items() if key not in old]
    if not additions:
        raise SystemExit('extension has no new records')
    target.write_bytes(b''.join(line for line in old_lines if json.loads(line)['key'] != 'complete') +
                       b''.join(additions) + old['complete'])
    print(f'Extended reference by {len(additions)} records; existing records unchanged')
    return 0


def verify(candidate):
    reference = reference_file(ROOT, 'jsonl')
    return subprocess.run([sys.executable, str(ROOT/'compare.py'), str(reference), str(candidate)]).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'verify', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--extend-reference', action='store_true', help='publish only new keys; existing records must be byte-identical')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return engine(args.label)
    if args.mode == 'publish':
        return publish(args.labels, args.extend_reference)
    if args.mode == 'check':
        label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
        return engine(label) or verify(WORK/label/OUT)
    return verify(args.candidate or WORK/'final'/OUT)


if __name__ == '__main__':
    sys.exit(main())
