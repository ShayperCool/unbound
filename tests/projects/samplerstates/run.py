#!/usr/bin/env python3
"""Records the sampler state probe in the Unity Editor or runs it in Unbound.

    run.py native --label L       one Editor capture (OpenGL Core, on the GPU through VirtualGL)
    run.py publish L1 L2          checks that two Editor captures agree, writes Reference/unity-R.jsonl
    run.py engine --label L       an Unbound capture (UNBOUND_CLI, else UNBOUND, else target/release)
    run.py check                  an Unbound capture compared with the reference (the land gate)

The probe's script and shaders are staged over a fresh copy of the UI Toolkit fixture
(tests/projects/uitk: its scene runs the script) under
$XDG_CACHE_HOME/unbound/validation/runs/samplerstates/<label>; nothing is written here except by
publish, which refuses to overwrite a reference. The release is $UNBOUND_PROBE_UNITY_VERSION
(else tests/probe_paths.py's default). Every missing, extra and failing value is printed; the
keys residuals.json lists are printed with their reason and do not fail the check.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
sys.path.insert(0, str(ROOT.parent/'nativegfx'))
from probe_paths import editor_path, reference_file, run_root, unity_version
import compare

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def stage(label):
    work = run_root(ROOT, label)
    if work.exists():
        sys.exit(f'refusing to overwrite run: {work}')
    project = work/'project'
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT.parent/'uitk'/part, project/part)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    shutil.copy2(ROOT/'Assets/Scripts/SamplerStatesProbe.cs', project/'Assets/Scripts/UitkProbe.cs')
    shutil.copytree(ROOT/'Assets/Shaders', project/'Assets/Shaders')
    return work, project


def capture(label, command, kind):
    work, project = stage(label)
    env = dict(os.environ, SAMPLER_STATES_OUT=str(work/'capture'))
    with (work/'process.log').open('w') as log:
        code = subprocess.run([str(LAUNCHER), '--kind', kind, 'timeout', '-s', 'KILL', '1500', *command(work, project)],
                              env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    values = work/'capture/values.jsonl'
    if code or not values.is_file() or '"complete"' not in values.read_text():
        sys.exit(f'{label}: exit {code}, capture incomplete; see {work}')
    return values


def native(label):
    values = capture(label, lambda work, project: [
        'xvfb-run', '-a', 'vglrun', '-d', 'egl', str(editor_path()), '-batchmode', '-projectPath', str(project),
        '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work/'editor.log')], 'editor-gpu')
    log = (values.parents[1]/'editor.log').read_text(errors='replace')
    if 'NVIDIA' not in next((line for line in log.splitlines() if 'Renderer:' in line), ''):
        sys.exit(f'{label}: the Editor did not run on the GPU')
    print('native', values)
    return 0


def engine(label):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    values = capture(label, lambda work, project: [
        cli, 'run', '-p', str(project), '--no-watch', '--frames', '35', '--size', '320x180', '--force-glcore'],
        'engine-gpu')
    print('engine', values)
    return values


def publish(labels):
    first, second = (run_root(ROOT, label)/'capture/values.jsonl' for label in labels)
    if first.read_bytes() != second.read_bytes():
        sys.exit('the two Editor captures differ; not publishing')
    target = reference_file(ROOT, 'jsonl')
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(first, target)
    print('published', target)
    return 0


def check():
    values = engine(f'check-{time.time_ns()}')
    compare.RESIDUALS = ROOT/'residuals.json'
    sys.argv = ['compare.py', str(reference_file(ROOT, 'jsonl')), str(values)]
    return compare.main()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    if args.mode == 'publish':
        if len(args.labels) != 2:
            parser.error('publish takes two native labels')
        return publish(args.labels)
    if args.mode == 'check':
        return check()
    if not args.label or Path(args.label).name != args.label:
        parser.error('--label must be a single directory name')
    if args.mode == 'native':
        return native(args.label)
    engine(args.label)
    return 0


if __name__ == '__main__':
    sys.exit(main())
