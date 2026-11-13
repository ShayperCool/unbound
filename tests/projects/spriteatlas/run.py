#!/usr/bin/env python3
"""Author each set once; capture twice independently; publish; check committed fixtures without an Editor.

    run.py author  --set v1|v2 --label L   the Editor authors the set's project (recorded in Authored/)
    run.py native  --set v1|v2 --label L   an Editor capture in Play mode
    run.py publish --set v1|v2 L1 L2       checks two captures agree, copies one to Reference/
    run.py check   [--set v1|v2]           engine captures against Reference/ (both sets by default)
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
sys.path.insert(0, str(ROOT))
import authored_project
import editor_runner
from compare import compare
from probe_paths import run_root, unity_version

WORK = run_root(ROOT, 'runs')
OUT = 'capture.json'
# v1: Sprite Atlas V1 assets, packer mode "Sprite Atlas V1 - Always Enabled"; v2: Sprite Atlas
# V2 assets, packer mode "Sprite Atlas V2 - Enabled".
SETS = ('v1', 'v2')


def reference(atlas_set):
    return ROOT/'Reference'/f'unity-{unity_version()}'/f'{atlas_set}.json'


def fixture(project):
    for part in authored_project.PARTS:
        shutil.copytree(ROOT/part, project/part)


def native(atlas_set, label, author=False):
    out = WORK/f'{atlas_set}-{label}'
    if out.exists():
        raise SystemExit(f'{out} already exists')
    project = out/'project'
    if author:
        fixture(project)
    else:
        authored_project.assemble(ROOT.name, unity_version(), atlas_set, project)
    # Recording enters Play mode, where the probe writes its capture and exits the Editor.
    arguments = ['-quit', '-executeMethod', 'SpriteAtlasSetup.Author'] if author else ['-executeMethod', 'SpriteAtlasSetup.Record']
    job = editor_runner.EditorJob(project, out/'editor.log', out, arguments, batch=True, graphics=True,
                                  env={'SPRITE_ATLAS_OUT': str(out/OUT), 'SPRITE_ATLAS_SET': atlas_set},
                                  timeout=1200, label=f'spriteatlas-{atlas_set}-{label}')
    result = editor_runner.run(job)
    print(editor_runner.summary(result), flush=True)
    if result['exit_code'] != 0:
        return 1
    if author:
        authored_project.record(ROOT.name, unity_version(), atlas_set, project,
                                f'python3 tests/projects/spriteatlas/run.py author --set {atlas_set} --label {label}')
    return 0


def publish(atlas_set, labels):
    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)

    def complete(directory):
        if not (directory/OUT).is_file() or json.loads((directory/OUT).read_text()).get('complete') is not True:
            return 'capture completion marker is missing'
    return editor_runner.publish([WORK/f'{atlas_set}-{label}' for label in labels], reference(atlas_set),
                                 lambda d: [OUT], write, complete=[complete])


def check(atlas_set):
    out = WORK/(time.strftime(f'check-{atlas_set}-%Y%m%d-%H%M%S-')+str(os.getpid()))
    project = out/'project'
    authored_project.assemble(ROOT.name, unity_version(), atlas_set, project)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    command = [str(editor_runner.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli,
               'run', '-p', str(project), '--no-watch', '--frames', '12']
    env = dict(os.environ, SPRITE_ATLAS_OUT=str(out/OUT), SPRITE_ATLAS_SET=atlas_set)
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    if code or not (out/OUT).is_file():
        print(f'engine exit {code}; missing capture: {not (out/OUT).is_file()}; {out}/engine.log')
        return 1
    return compare(json.loads(reference(atlas_set).read_text()), json.loads((out/OUT).read_text()), out,
                   f'spriteatlas {atlas_set}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['author', 'native', 'publish', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--set', dest='atlas_set', choices=SETS)
    parser.add_argument('--label')
    args = parser.parse_args()
    if args.mode in ('author', 'native'):
        if not args.label or not args.atlas_set:
            parser.error('--set and --label are required')
        code = native(args.atlas_set, args.label, args.mode == 'author')
    elif args.mode == 'publish':
        if not args.atlas_set:
            parser.error('--set is required')
        code = publish(args.atlas_set, args.labels)
    else:
        code = max(check(s) for s in ([args.atlas_set] if args.atlas_set else SETS))
    sys.exit(code)
