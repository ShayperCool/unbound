#!/usr/bin/env python3
"""Records the NavMesh probe in the Unity Editor, or runs it in Unbound and checks the carving.

    run.py editor  [--label L]   two independent Editor captures (native1, native2)
    run.py publish [--label L]   checks that both agree and writes Reference/unity-<release>.json
    run.py unbound [--label L]   an Unbound capture ($UNBOUND or UNBOUND_CLI: the binary), then
                                 compare.py over everything (a report) and the carving check
                                 (the exit status)

Captures go to $XDG_CACHE_HOME/unbound/validation/runs/navmesh/<label>; each Editor runs on a
fresh copy of the probe through tests/editor_runner.py (one launcher lease each, GPU through
VirtualGL). Do not wrap this driver in with-heavy-job.

The carving check compares, value by value with compare.py's tolerance, what a carving
NavMeshObstacle decides: the carve.* scenarios (hole outlines and heights, the NavMesh vertices
around them) and each frame's `holes` (where the NavMesh is carved away, seen from Update and
LateUpdate); these decide the exit status. It also lists every difference of the `blocked` agent,
whose lane the carver blocks, and of its corners: its route past the carved hole is chosen by the
path search (a residual of Unbound's A* costs, docs/COMPATIBILITY_GAPS.md), so those are reported,
not gated. The other agents' differences are in compare.py's full report.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file, run_root, unity_version
from editor_runner import EditorJob, result_problem, run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPEATS = ('native1', 'native2')
OUTPUT = 'navmesh.json'
TOLERANCE = 1e-3


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    return dest


def editor(args):
    root = run_root(PROBE, args.label)
    jobs = []
    for name in REPEATS:
        out = root/name
        if out.exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        project = copy_project(out/'project')
        jobs.append(EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'], batch=True,
                              env={'NAVMESH_PROBE_OUT': str(out/OUTPUT)}, timeout=1200, label='navmesh-'+name))
    failed = False
    for result, name in zip(run_parallel(jobs), REPEATS):
        print(summary(result))
        problem = result_problem(root/name)
        if problem or not (root/name/OUTPUT).exists():
            print(f'{name}: {problem or "no output"}')
            failed = True
    return 1 if failed else 0


def publish(args):
    root = run_root(PROBE, args.label)
    for name in REPEATS:
        problem = result_problem(root/name)
        if problem:
            sys.exit(f'{name}: {problem}')
    first, second = ((root/name/OUTPUT).read_bytes() for name in REPEATS)
    if first != second:
        sys.exit('the two captures differ; nothing published')
    target = reference_file(PROBE)
    target.write_bytes(first)
    print(f'published {target}')
    return 0


def carving_values(data, keys):
    """The carve.* scenarios (when 'carve' is among the keys) and the agents' per-frame values
    named by `keys`: scenario -> {key: value}."""
    picked = {}
    for scenario, values in data.items():
        if scenario.startswith('carve.'):
            if 'carve' in keys:
                picked[scenario] = dict(values)
        elif scenario.startswith('agents.f'):
            keep = {k: v for k, v in values.items() if k in keys}
            if keep:
                picked[scenario] = keep
    return picked


def same(a, b):
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        return abs(a - b) <= TOLERANCE * max(1.0, abs(a))
    return a == b


def differences(reference, candidate, keys):
    ref, cand = carving_values(reference, keys), carving_values(candidate, keys)
    problems = []
    for scenario, values in ref.items():
        other = cand.get(scenario, {})
        for key, a in values.items():
            b = other.get(key, '<missing>')
            if not same(a, b):
                problems.append(f'{scenario}.{key}: unity {a!r}  unbound {b!r}')
        for key in other:
            if key not in values:
                problems.append(f'{scenario}.{key}: unity <missing>  unbound {other[key]!r}')
    for scenario in cand:
        if scenario not in ref:
            problems.append(f'{scenario}: unity <missing>')
    return problems, sum(len(v) for v in ref.values())


def check(reference, candidate):
    route, count = differences(reference, candidate, ('blocked', 'blocked.corners'))
    for line in route:
        print(line)
    print(f'blocked agent (reported, not gated): {len(route)} of {count} values differ')
    problems, count = differences(reference, candidate, ('carve', 'holes'))
    for line in problems:
        print(line)
    print(f'carving check: {len(problems)} of {count} values differ')
    return 1 if problems else 0


def unbound(args):
    binary = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI')
    if not binary:
        sys.exit('set UNBOUND to the unbound binary')
    out = run_root(PROBE, args.label)/'unbound'
    out.mkdir(parents=True, exist_ok=True)
    result = out/OUTPUT
    result.unlink(missing_ok=True)
    launcher = Path.home()/'.cache/compat/with-heavy-job'
    command = [binary, 'run', '-p', str(PROBE), '--no-watch', '--frames', '300']
    if launcher.exists():
        command = [str(launcher), *command]
    with open(out/'run.log', 'w') as log:
        code = subprocess.run(command, env=dict(os.environ, NAVMESH_PROBE_OUT=str(result)), stdout=log,
                              stderr=subprocess.STDOUT, timeout=900).returncode
    if code != 0 or not result.exists():
        sys.exit(f'Unbound failed (exit {code}); see {out/"run.log"}')
    reference = reference_file(PROBE)
    report = subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference), str(result), '--details', '0'],
                            capture_output=True, text=True).stdout
    (out/'compare.txt').write_text(report)
    print(report.strip().splitlines()[-1] + f' (full report: {out/"compare.txt"})')
    return check(json.loads(reference.read_text()), json.loads(result.read_text()))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--label', default='current')
    args = parser.parse_args()
    print(f'Unity {unity_version()}', file=sys.stderr)
    return {'editor': editor, 'publish': publish, 'unbound': unbound}[args.command](args)


if __name__ == '__main__':
    sys.exit(main())
