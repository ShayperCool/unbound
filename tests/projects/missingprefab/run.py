#!/usr/bin/env python3
"""References into nested prefab instances whose source prefab is present or missing.

    run.py author  --release R --label L     authors Assets/Probe (prefabs, scene) in a batch Editor
    run.py editor  --release R --label L     two windowed GPU Editor captures of each variant
    run.py publish --release R --label L     publishes the identical captures to Reference/unity-R/
    run.py check   --release R [--label L]   runs Unbound (UNBOUND_CLI) and compares with Reference/
                                         (exactly; residuals.json lists the recorded differences)

Variants: `present` keeps the nested prefab Assets/Probe/Inner.prefab, `missing` deletes it (asset
and .meta) from the private project copy before the Editor or Unbound opens it.
Run output goes to $XDG_CACHE_HOME/unbound/validation/runs/missingprefab/<label>.
"""
import argparse
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run, run_parallel, summary, vulkan_display_env  # noqa: E402
from probe_paths import run_root  # noqa: E402
spec = importlib.util.spec_from_file_location('edit_mode_runner', PROBE.parent / 'editinheritance/run.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
shared.PROBE = PROBE

VARIANTS = ('present', 'missing')
REPEATS = ('native1', 'native2')
MANIFEST = '{"dependencies":{"com.unity.modules.physics":"1.0.0"}}'


def prepare(project, release, variant):
    shared.prepare(project, release)
    settings = project / 'ProjectSettings/EditorBuildSettings.asset'
    guid = (PROBE / 'Assets/Probe/Probe.unity.meta').read_text().split('guid: ')[1].splitlines()[0]
    settings.write_text(settings.read_text().replace('  m_configObjects: {}', '  - enabled: 1\n    path: Assets/Probe/Probe.unity\n    guid: GUID\n  m_configObjects: {}'.replace('GUID', guid)))
    (project / 'Packages/manifest.json').write_text(MANIFEST)
    if variant == 'missing':
        for name in ('Inner.prefab', 'Inner.prefab.meta'):
            (project / 'Assets/Probe' / name).unlink()


def editor_job(project, out, release, *, batch=False, args=()):
    env = vulkan_display_env()
    if not batch:
        env['MISSING_PREFAB_OUT'] = str(out / 'values.jsonl')
    return EditorJob(project, out / 'editor.log', out, ['-force-glcore', *args], private_display=False,
                     batch=batch, graphics=True, timeout=600, label='missingprefab-' + out.parent.name + '-' + out.name,
                     env=env, launch_inputs={'release': release, 'paths': {
                         'assets': str(project / 'Assets'), 'settings': str(project / 'ProjectSettings'),
                         'packages': str(project / 'Packages'), 'runner': str(PROBE / 'run.py')}})


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('mode', choices=('author', 'editor', 'publish', 'check'))
    p.add_argument('--release', required=True)
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    a = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
    work = run_root(PROBE, a.label)
    reference = PROBE / 'Reference' / ('unity-' + a.release)

    if a.mode == 'author':
        project, out = work / 'projects' / 'author', work / 'author'
        shared.prepare(project, a.release)
        (project / 'Packages/manifest.json').write_text(MANIFEST)
        shutil.copy2(PROBE / 'Authoring/MissingPrefabAuthor.cs', project / 'Assets/Editor/MissingPrefabAuthor.cs')
        out.mkdir(parents=True)
        result = run(editor_job(project, out, a.release, batch=True,
                                args=['-executeMethod', 'MissingPrefabAuthor.Author']))
        print(summary(result))
        authored = project / 'Assets/Probe'
        if not (authored / 'Probe.unity').exists():
            raise SystemExit('the Editor authored nothing')
        target = PROBE / 'Assets/Probe'
        shutil.rmtree(target, ignore_errors=True)
        shutil.copytree(authored, target)
        (PROBE / 'Assets/Probe.meta').unlink(missing_ok=True)
        if (project / 'Assets/Probe.meta').exists():
            shutil.copy2(project / 'Assets/Probe.meta', PROBE / 'Assets/Probe.meta')
        return 0

    if a.mode == 'publish':
        reference.mkdir(parents=True, exist_ok=True)
        for variant in VARIANTS:
            captures = [work / variant / r for r in REPEATS]
            for capture in captures:
                problem = shared.capture_problem(capture)
                if problem:
                    raise SystemExit(f'{capture}: {problem}')
            first, second = [(c / 'values.jsonl').read_bytes() for c in captures]
            if first != second:
                raise SystemExit(f'{variant}: Editor repeats differ')
            with (reference / (variant + '.jsonl')).open('xb') as f:
                f.write(first)
        return 0

    if a.mode == 'editor':
        jobs = []
        for variant in VARIANTS:
            for repeat in REPEATS:
                project, out = work / 'projects' / variant / repeat, work / variant / repeat
                prepare(project, a.release, variant)
                out.mkdir(parents=True)
                jobs.append(editor_job(project, out, a.release))
        for result in run_parallel(jobs):
            print(summary(result))
        failed = 0
        for variant in VARIANTS:
            for repeat in REPEATS:
                problem = shared.capture_problem(work / variant / repeat)
                if problem:
                    print(f'{variant}/{repeat}: {problem}')
                    failed = 1
            if not failed and (work / variant / 'native1/values.jsonl').read_bytes() != (work / variant / 'native2/values.jsonl').read_bytes():
                print(f'{variant}: Editor repeats differ')
                failed = 1
        return failed

    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
    if not cli:
        p.error('set UNBOUND_CLI to an immutable runtime')
    failed = 0
    for variant in VARIANTS:
        project, out = work / 'projects' / variant / 'unbound', work / variant / 'unbound'
        prepare(project, a.release, variant)
        out.mkdir(parents=True)
        env = dict(os.environ, MISSING_PREFAB_OUT=str(out / 'values.jsonl'))
        with (out / 'engine.log').open('w') as f:
            code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                                   'timeout', '240', cli, 'test', '-p', str(project), '--platform', 'EditMode'],
                                  env=env, stdout=f, stderr=subprocess.STDOUT).returncode
        if code:
            print(f'missingprefab {variant}: engine exited {code} (log {out / "engine.log"})')
            failed = 1
            continue
        failed |= subprocess.run([sys.executable, str(PROBE / 'compare.py'), str(reference / (variant + '.jsonl')),
                                  str(out / 'values.jsonl'), '--variant', variant]).returncode
    return failed


if __name__ == '__main__':
    raise SystemExit(main())
