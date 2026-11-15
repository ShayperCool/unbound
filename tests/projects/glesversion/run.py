#!/usr/bin/env python3
"""Graphics.minOpenGLESVersion for the Android target under player-setting variants, in private
project copies (one Editor or engine process per variant).

    run.py editor  --release R --label L       two independent batch GPU Editor captures per variant
    run.py publish --release R --label L       publishes the identical captures to Reference/unity-R.json
    run.py unbound --release R [--label L]     runs Unbound per variant and compares with Reference/ (exact)

Run output goes to $XDG_CACHE_HOME/unbound/validation/runs/glesversion/<label>.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run, summary, result_problem  # noqa: E402
from probe_paths import run_root  # noqa: E402

REPEATS = ('native1', 'native2')
BASE = REPO / 'tests/projects/physics'
VULKAN, GLES3 = '15000000', '0b000000'
# variant -> (openGLRequireES31, openGLRequireES31AEP, openGLRequireES32, Android API list or None, m_Automatic)
VARIANTS = {
    'automatic': (0, 0, 0, None, 1),
    'es31': (1, 0, 0, None, 1),
    'es31aep': (1, 1, 0, None, 1),
    'es32': (1, 1, 1, None, 1),
    'aep-only': (0, 1, 0, None, 1),
    'es32-only': (0, 0, 1, None, 1),
    'vulkan-only': (1, 0, 0, VULKAN, 0),
    'gles-only': (0, 0, 1, GLES3, 0),
    'vulkan-gles': (1, 1, 0, VULKAN + GLES3, 0),
    'vulkan-only-automatic': (1, 0, 0, VULKAN, 1),
}


def prepare(project, release, variant):
    if project.exists():
        raise SystemExit(f'project already exists: {project}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(BASE / part, project / part)
    shutil.copy2(PROBE / 'Sources/ProbeEntry.cs', project / 'Assets/Scripts/ProbeEntry.cs')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + release + '\n')
    es31, aep, es32, apis, automatic = VARIANTS[variant]
    path = project / 'ProjectSettings/ProjectSettings.asset'
    text = path.read_text()
    for key, value in (('openGLRequireES31', es31), ('openGLRequireES31AEP', aep), ('openGLRequireES32', es32)):
        text, count = re.subn(rf'^  {key}: \d+$', f'  {key}: {value}', text, flags=re.M)
        assert count == 1, key
    if apis is not None:
        entry = (f'  m_BuildTargetGraphicsAPIs:\n  - m_BuildTarget: AndroidPlayer\n    m_APIs: {apis}\n'
                 f'    m_Automatic: {automatic}')
        text, count = re.subn(r'^  m_BuildTargetGraphicsAPIs: \[\]$', entry, text, flags=re.M)
        assert count == 1
    path.write_text(text)


def capture_problem(directory):
    problem = result_problem(directory, gpu=True)
    if problem:
        return problem
    result = json.loads((directory / 'editor-run.json').read_text())
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 'capture did not exit normally'
    return None if (directory / 'values.json').exists() else 'no record'


def job(project, out):
    return EditorJob(project, out / 'editor.log', out, args=['-buildTarget', 'Android', '-executeMethod', 'ProbeRunner.Record'],
                     batch=True, graphics=True, timeout=600, label='glesversion-' + out.parent.name + '-' + out.name,
                     env={'PHYSICS_PROBE_OUT': str(out / 'values.json')},
                     progress_files=[out / 'values.json'], progress_timeout=300)


def collect(work, repeat):
    return {variant: json.loads((work / variant / repeat / 'values.json').read_text()) for variant in VARIANTS}


def compare(reference, actual):
    failures = [(v, reference.get(v), actual.get(v)) for v in sorted(reference.keys() | actual.keys())
                if reference.get(v) != actual.get(v)]
    for variant, want, got in failures:
        print('FAIL', variant, 'reference', want, 'unbound', got)
    print(f'glesversion: {len(reference) - len(failures)}/{len(reference)} variants match')
    return 1 if failures else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    p.add_argument('--release', required=True)
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    args = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    reference = PROBE / 'Reference' / ('unity-' + args.release + '.json')
    if args.mode == 'editor':
        for variant in VARIANTS:
            for repeat in REPEATS:
                project, out = work / variant / ('project-' + repeat), work / variant / repeat
                prepare(project, args.release, variant)
                print(summary(run(job(project, out))), flush=True)
                problem = capture_problem(out)
                if problem:
                    raise SystemExit(f'{variant}/{repeat}: {problem}')
        return compare(collect(work, 'native1'), collect(work, 'native2'))
    if args.mode == 'publish':
        first, second = collect(work, 'native1'), collect(work, 'native2')
        if first != second:
            raise SystemExit('the two captures differ')
        reference.parent.mkdir(exist_ok=True)
        with reference.open('x') as stream:
            stream.write(json.dumps(first, indent=2, sort_keys=True) + '\n')
        return 0
    cli = os.environ.get('UNBOUND', os.environ.get('UNBOUND_CLI', str(REPO / 'target/release/unbound')))
    for variant in VARIANTS:
        project, out = work / variant / 'project-unbound', work / variant / 'unbound'
        prepare(project, args.release, variant)
        out.mkdir(parents=True)
        with (out / 'engine.log').open('w') as log:
            code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '600',
                                   cli, 'run', '-p', str(project), '--target', 'Android', '--headless', '30000', '--no-watch'],
                                  env=dict(os.environ, PHYSICS_PROBE_OUT=str(out / 'values.json')),
                                  stdout=log, stderr=subprocess.STDOUT).returncode
        if code or not (out / 'values.json').exists():
            print((out / 'engine.log').read_text()[-3000:])
            return code or 1
    return compare(json.loads(reference.read_text()), collect(work, 'unbound'))


if __name__ == '__main__':
    sys.exit(main())
