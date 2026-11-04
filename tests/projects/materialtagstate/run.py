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
import re
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
# Child environments are built from an empty mapping: these key names are copied when present, then the
# command's own values are set. Nothing else is inherited and no value is printed.
TOOL_ENV_KEYS = ('PATH', 'HOME', 'LANG', 'LC_ALL', 'TZ')
EDITOR_ENV_KEYS = ('PATH', 'HOME', 'USER', 'LOGNAME', 'LANG', 'LC_ALL', 'TZ', 'TMPDIR', 'XDG_CACHE_HOME', 'XDG_RUNTIME_DIR')
CREDENTIAL_NAME = re.compile(r'TOKEN|SECRET|PASSW|API_?KEY|CREDENTIAL|PRIVATE_?KEY|AUTHORIZATION|COOKIE|SESSION', re.I)
CREDENTIAL_VALUE = re.compile(r'(?i)\bbearer\s|\bsk-[a-z0-9-]{8,}|\bghp_|\bgithub_pat_|\bxox[abp]-|\bAKIA[0-9A-Z]{16}')


def explicit_env(keys, **values):
    """A child environment from an empty mapping; raises (naming keys only) on a credential-like entry."""
    env = {k: os.environ[k] for k in keys if k in os.environ}
    env.update(values)
    bad = sorted(k for k, v in env.items() if CREDENTIAL_NAME.search(k) or CREDENTIAL_VALUE.search(v))
    if bad:
        sys.exit('refusing a child environment with credential-like entries: ' + ', '.join(bad))
    return env


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
            'env_key_names': sorted(set(EDITOR_ENV_KEYS) | {'DISPLAY', 'MATERIAL_TAG_STATE_OUT', 'MATERIAL_TAG_STATE_DIAG'}),
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
    # Refuse used outputs before writing a plan, composing a project or starting Xvfb.
    if args.execute:
        for step in steps:
            project = Path(step['project'])
            output = Path(step['env']['MATERIAL_TAG_STATE_OUT']).parent
            if project.exists() or project.is_symlink() or output.exists() or output.is_symlink():
                sys.exit(f"{step['repeat']}: run folder already used; choose a new --label")
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
            env = explicit_env(EDITOR_ENV_KEYS, DISPLAY=name, **{k: v for k, v in step['env'].items() if k != 'DISPLAY'})
            print(step['repeat'], 'child environment keys', sorted(env))
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
    env = explicit_env(TOOL_ENV_KEYS, PYTHONDONTWRITEBYTECODE='1')
    code = 0
    for repeat in ('native1', 'native2'):
        log_path = work/repeat/'editor.log'
        diag_path = work/repeat/'diagnostics.json'
        log = log_path.read_text(errors='replace') if log_path.is_file() else ''
        renderers = [line.strip() for line in log.splitlines() if line.strip().startswith('Renderer:')]
        try:
            diag = json.loads(diag_path.read_text())
        except (OSError, ValueError):
            diag = {}
        evidence = (log + '\n' + str(diag.get('graphicsDeviceName', ''))).casefold()
        software = any(name in evidence for name in
                       ('null device', 'llvmpipe', 'lavapipe', 'softpipe', 'swiftshader', 'forcing gfxdevice: null'))
        hardware = any('nvidia' in line.casefold() for line in renderers)
        backend = diag.get('graphicsDeviceType') in ('OpenGLCore', 'Vulkan')
        print(repeat, renderers, diag.get('graphicsDeviceType'), diag.get('graphicsDeviceName'))
        if not hardware or not backend or software:
            code = 1
            print(f'{repeat}: INVALID not a hardware NVIDIA GPU Editor session')
        code |= subprocess.run(compare + ['classify', str(work/repeat/'capture.jsonl')], env=env).returncode
    code |= subprocess.run(compare + ['pair', str(work/'native1/capture.jsonl'), str(work/'native2/capture.jsonl')], env=env).returncode
    saved = validation_cache()/'runs'/SAVED_MAPS
    subprocess.run(compare + ['saved', str(work/'native1/capture.jsonl'), str(saved)], env=env)
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
