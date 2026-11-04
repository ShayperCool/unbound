#!/usr/bin/env python3
"""Composes and (only under an explicit release) records the material tag state probe.

    run.py compose   --release R --label L   fresh project under the validation cache, checked against inputs.json
    run.py inventory --release R --label L   composes and writes the inventory (deliberate refresh of inputs.json)
    run.py editor    --release R --label L   writes the two-repeat Editor plan; launches only with --execute
                                             and MATERIAL_TAG_STATE_GRANT naming an existing Root release file
    run.py check     --release R --label L   compare.py validate/classify each repeat, pair them, report saved maps

The project is exactly the model import probe's URP composition (modelimport/run.py
copy_project, so the same importer settings, URP asset and package lock) plus the fixture
script beside ProbeRunner.cs. Every composed file is checked against inputs.json before any
launch. Editors are GPU Editors (private Xvfb, VirtualGL, no -nographics), one at a time,
through the shared heavy-job launcher. Captures, diagnostics and logs stay in the cache.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import cache_artifact_path, editor_path, run_root, validation_cache  # noqa: E402

MODELIMPORT = REPO/'tests/projects/modelimport'
URP = REPO/'tests/projects/urp'
FIXTURE_FILES = ['Assets/Editor/MaterialTagStateDiscriminator.cs', 'Assets/Editor/MaterialTagStateDiscriminator.cs.meta']
INPUTS = PROBE/'inputs.json'
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
SAVED_MAPS = 'root-receipt-triage-20261104/m1/saved-native-materials.json'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def modelimport_copy_project():
    spec = importlib.util.spec_from_file_location('modelimport_run', MODELIMPORT/'run.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.copy_project


def compose(dest):
    cache_artifact_path(dest)
    modelimport_copy_project()(dest, 'URP')
    for rel in FIXTURE_FILES:
        shutil.copyfile(PROBE/rel, dest/rel)
    return dest


def inventory(project):
    rows = []
    for p in sorted(project.rglob('*')):
        if not p.is_file():
            continue
        rel = str(p.relative_to(project))
        data = p.read_bytes()
        if rel in FIXTURE_FILES:
            source = 'fixture: tests/projects/materialtagstate/' + rel
        elif (MODELIMPORT/rel).is_file() and (MODELIMPORT/rel).read_bytes() == data:
            source = 'exact copy: tests/projects/modelimport/' + rel
        elif (URP/rel).is_file() and (URP/rel).read_bytes() == data:
            source = 'exact copy: tests/projects/urp/' + rel
        else:
            source = 'derived by modelimport/run.py copy_project (URP profile)'
        rows.append({'path': rel, 'sha256': sha(data), 'source': source})
    return rows


def check_inputs(project):
    want = {r['path']: r['sha256'] for r in json.loads(INPUTS.read_text())['files']}
    got = {r['path']: r['sha256'] for r in inventory(project)}
    problems = [f'missing {p}' for p in sorted(set(want) - set(got))]
    problems += [f'extra {p}' for p in sorted(set(got) - set(want))]
    problems += [f'differs {p}' for p in sorted(set(want) & set(got)) if want[p] != got[p]]
    if problems:
        sys.exit('composed project does not match inputs.json:\n  ' + '\n  '.join(problems))
    return len(got)


def display():
    for number in range(91, 120):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def plan(work):
    steps = []
    for repeat in ('native1', 'native2'):
        out = work/repeat
        steps.append({
            'repeat': repeat, 'project': str(work/'projects'/repeat),
            'env': {'MATERIAL_TAG_STATE_OUT': str(out/'capture.jsonl'), 'MATERIAL_TAG_STATE_DIAG': str(out/'diagnostics.json'),
                    'DISPLAY': '<private Xvfb>'},
            'argv': [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
                     '-batchmode', '-projectPath', str(work/'projects'/repeat),
                     '-executeMethod', 'MaterialTagStateDiscriminator.Run', '-logFile', str(out/'editor.log')]})
    return steps


def cmd_compose(work, _args):
    project = compose(work/'projects'/'composed')
    print(project, check_inputs(project), 'files match inputs.json')
    return 0


def cmd_inventory(work, _args):
    project = compose(work/'projects'/'inventory')
    rows = inventory(project)
    target = work/'inputs.json'
    target.write_text(json.dumps({'composition': 'modelimport copy_project URP + fixture', 'files': rows}, indent=1) + '\n')
    print(target, len(rows), 'files')
    return 0


def cmd_editor(work, args):
    steps = plan(work)
    work.mkdir(parents=True, exist_ok=True)
    (work/'editor-plan.json').write_text(json.dumps(steps, indent=2) + '\n')
    print(json.dumps(steps, indent=2))
    if not args.execute:
        print('plan only: nothing launched')
        return 0
    grant = os.environ.get('MATERIAL_TAG_STATE_GRANT')
    if not grant or not Path(grant).is_file():
        sys.exit('--execute needs MATERIAL_TAG_STATE_GRANT naming an existing Root release file')
    name, server = display()
    codes = []
    try:
        for step in steps:
            project = compose(Path(step['project']))
            check_inputs(project)
            out = Path(step['env']['MATERIAL_TAG_STATE_OUT']).parent
            out.mkdir(parents=True, exist_ok=False)
            env = dict(os.environ, DISPLAY=name, **{k: v for k, v in step['env'].items() if k != 'DISPLAY'})
            code = subprocess.run(step['argv'], env=env).returncode
            diag = out/'diagnostics.json'
            if diag.exists():
                (out/'diagnostics.sha256').write_text(sha(diag.read_bytes()) + '  diagnostics.json\n')
            codes.append(code)
            print(step['repeat'], 'editor exit', code)
    finally:
        server.terminate()
    return int(any(codes))


def cmd_check(work, _args):
    compare = [sys.executable, '-B', str(PROBE/'compare.py')]
    code = 0
    for repeat in ('native1', 'native2'):
        code |= subprocess.run(compare + ['classify', str(work/repeat/'capture.jsonl')]).returncode
    code |= subprocess.run(compare + ['pair', str(work/'native1/capture.jsonl'), str(work/'native2/capture.jsonl')]).returncode
    saved = validation_cache()/'runs'/SAVED_MAPS
    subprocess.run(compare + ['saved', str(work/'native1/capture.jsonl'), str(saved)])
    return code


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('mode', choices=('compose', 'inventory', 'editor', 'check'))
    ap.add_argument('--label', required=True)
    ap.add_argument('--release', required=True)
    ap.add_argument('--execute', action='store_true', help='editor: launch (needs MATERIAL_TAG_STATE_GRANT)')
    ap.add_argument('--work', type=Path, help='run folder (default: the validation cache run root for --label)')
    args = ap.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        ap.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = cache_artifact_path(args.work) if args.work else run_root(PROBE, args.label)
    return {'compose': cmd_compose, 'inventory': cmd_inventory, 'editor': cmd_editor, 'check': cmd_check}[args.mode](work, args)


if __name__ == '__main__':
    sys.exit(main())
