#!/usr/bin/env python3
"""Particle collision events (colliderComponent, the safe event size) and force-field vector fields
(Play mode, private project copies).

    run.py editor  --release R --label L     two windowed GPU Editor captures
    run.py publish --release R --label L     publishes the identical captures to Reference/unity-R.jsonl
    run.py check   --release R [--label L]   runs Unbound (UNBOUND_CLI) and compares with Reference/

Run output goes to $XDG_CACHE_HOME/unbound/validation/runs/particlecollision/<label>.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run_parallel, summary, vulkan_display_env  # noqa: E402
from probe_paths import run_root  # noqa: E402
import value_probe  # noqa: E402
spec = importlib.util.spec_from_file_location('edit_mode_runner', PROBE.parent / 'editinheritance/run.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
shared.PROBE = PROBE

REPEATS = ('native1', 'native2')
MANIFEST = ('{"dependencies":{"com.unity.modules.particlesystem":"1.0.0","com.unity.modules.physics":"1.0.0",'
            '"com.unity.modules.physics2d":"1.0.0"}}')
# Field positions and velocities after five steps: float differences between implementations.
TOLERANCE = 1e-4


def prepare(project, release):
    shared.prepare(project, release)
    (project / 'Packages/manifest.json').write_text(MANIFEST)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('mode', choices=('editor', 'publish', 'check'))
    p.add_argument('--release', required=True)
    p.add_argument('--label', default='run-' + str(time.time_ns()))
    a = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
    work = run_root(PROBE, a.label)
    reference = PROBE / 'Reference' / ('unity-' + a.release + '.jsonl')
    if a.mode == 'publish':
        captures = [work / r for r in REPEATS]
        for capture in captures:
            problem = shared.capture_problem(capture)
            if problem:
                raise SystemExit(f'{capture}: {problem}')
        first, second = [(c / 'values.jsonl').read_bytes() for c in captures]
        if first != second:
            raise SystemExit('Editor repeats differ')
        reference.parent.mkdir(parents=True, exist_ok=True)
        with reference.open('xb') as f:
            f.write(first)
        return 0
    if a.mode == 'editor':
        jobs = []
        for repeat in REPEATS:
            project, out = work / 'projects' / repeat, work / repeat
            prepare(project, a.release)
            out.mkdir(parents=True)
            env = {**vulkan_display_env(), 'PARTICLE_COLLISION_OUT': str(out / 'values.jsonl')}
            jobs.append(EditorJob(project, out / 'editor.log', out, ['-force-glcore'], private_display=False,
                                  batch=False, graphics=True, timeout=600, label='particlecollision-' + repeat, env=env,
                                  launch_inputs={'release': a.release, 'paths': {
                                      'assets': str(project / 'Assets'), 'settings': str(project / 'ProjectSettings'),
                                      'packages': str(project / 'Packages'), 'runner': str(PROBE / 'run.py')}}))
        for result in run_parallel(jobs):
            print(summary(result))
        failed = 0
        for repeat in REPEATS:
            problem = shared.capture_problem(work / repeat)
            if problem:
                print(f'{repeat}: {problem}')
                failed = 1
        if not failed and (work / 'native1/values.jsonl').read_bytes() != (work / 'native2/values.jsonl').read_bytes():
            print('Editor repeats differ')
            failed = 1
        return failed
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
    if not cli:
        p.error('set UNBOUND_CLI to an immutable runtime')
    project, out = work / 'projects' / 'unbound', work / 'unbound'
    prepare(project, a.release)
    out.mkdir(parents=True)
    env = dict(os.environ, PARTICLE_COLLISION_OUT=str(out / 'values.jsonl'))
    with (out / 'engine.log').open('w') as f:
        code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                               'timeout', '300', cli, 'test', '-p', str(project), '--platform', 'EditMode'],
                              env=env, stdout=f, stderr=subprocess.STDOUT).returncode
    if code:
        print(f'particlecollision: engine exited {code} (log {out / "engine.log"})')
        return code
    return compare(reference, out / 'values.jsonl')


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    values = {row['k']: row['v'] for row in rows}
    if not rows or len(values) != len(rows):
        raise SystemExit(f'{path}: empty or duplicate record')
    return values


def compare(reference, actual):
    """Every missing, extra and differing key; residuals.json lists the known ones with their reason."""
    expected, got = load(reference), load(actual)
    matched, failures = value_probe.compare(expected, got, TOLERANCE)
    known = value_probe.residuals(PROBE)
    failing = 0
    for failure in failures:
        reason = known.get(failure['key'])
        print(json.dumps({**failure, 'residual': reason} if reason else failure))
        failing += reason is None
    for key in sorted(known):
        if key not in expected:
            print(f'FAIL {key}: a residual for a key the reference does not have')
            failing += 1
        elif got.get(key) == expected[key]:
            print(f'residual resolved (remove it from residuals.json): {key}')
    print(f'particlecollision: {matched}/{len(expected)} values match, {len(failures)} failures, {len(failures) - failing} known residuals')
    return int(failing > 0)


if __name__ == '__main__':
    raise SystemExit(main())
