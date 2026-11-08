#!/usr/bin/env python3
"""Record the Unity Editor (native), run Unbound (engine), publish and verify.

    run.py create --label author1          # Editor authors Assets/Resources/Authored.prefab (copied back)
    run.py create-gradients --label grad1  # Editor authors Assets/Resources/Gradients.prefab (copied back)
    run.py native --label native1          # a private project copy, GPU Editor through the job pool
    run.py publish native1 native2         # two identical captures become Reference/unity-<version>.jsonl
    run.py engine --label final            # Unbound (UNBOUND_CLI or target/release/unbound)
    run.py verify [--candidate PATH]       # compare against the reference (default: the engine run 'final')
    run.py check                           # engine run into a new directory, then verify (the landing gate)

Run output stays under $XDG_CACHE_HOME/unbound/validation/runs/particlecurves/.
"""
import argparse
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
AUTHORED = Path('Assets/Resources/Authored.prefab')
GRADIENTS = Path('Assets/Resources/Gradients.prefab')


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, target/folder)


def editor(label, method, env):
    out = WORK/label
    project = out/'project'
    copy_project(project)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.' + method],
                                  batch=True, graphics=True, env=env, timeout=1200, label='particlecurves-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    return out, project


def create(label, method='Create', prefab=AUTHORED):
    out, project = editor(label, method, {})
    for path in (prefab, prefab.with_suffix('.prefab.meta')):
        if not (project/path).exists():
            print('missing', path)
            return 1
        shutil.copy2(project/path, ROOT/path)
    return 0


def native(label):
    out = WORK/label
    editor(label, 'Record', {'PARTICLE_CURVES_OUT': str(out/OUT)})
    return 0 if (out/OUT).exists() else 1


def engine(label):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    out.mkdir(parents=True)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, PARTICLE_CURVES_OUT=str(out/OUT))
    command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '600', cli, 'run', '-p', str(ROOT), '--headless', '3000', '--no-watch']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(label, 'exit', code)
    return 0 if code == 0 and (out/OUT).exists() else 1


def publish(labels):
    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    return editor_runner.publish([WORK/label for label in labels], reference_file(ROOT, 'jsonl'), lambda d: [OUT], write,
                                 complete=[editor_runner.jsonl_complete(OUT, {'key': 'complete', 'value': [1]})])


def verify(candidate):
    reference = reference_file(ROOT, 'jsonl')
    return subprocess.run([sys.executable, str(ROOT/'compare.py'), str(reference), str(candidate),
                           '--known', str(ROOT/'known.json')]).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['create', 'create-gradients', 'native', 'engine', 'publish', 'verify', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    if args.mode == 'create':
        return create(args.label)
    if args.mode == 'create-gradients':
        return create(args.label, 'CreateGradients', GRADIENTS)
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return engine(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    if args.mode == 'check':
        label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
        return engine(label) or verify(WORK/label/OUT)
    return verify(args.candidate or WORK/'final'/OUT)


if __name__ == '__main__':
    sys.exit(main())
