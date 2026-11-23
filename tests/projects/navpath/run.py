#!/usr/bin/env python3
"""Records the NavMesh path-cost probe in the Unity Editor, or runs it in Unbound and compares.

    run.py editor  [--label L]   two independent Editor captures (native1, native2)
    run.py publish [--label L]   checks that both agree and writes Reference/unity-<release>.json
    run.py unbound [--label L]   an Unbound capture ($UNBOUND or UNBOUND_CLI: the binary), then the
                                 comparison with the reference, scenario by scenario

Captures go to $XDG_CACHE_HOME/unbound/validation/runs/navpath/<label>; each Editor runs on a fresh
copy of the probe through tests/editor_runner.py (one launcher lease each, GPU through VirtualGL).
Do not wrap this driver in with-heavy-job.

The comparison lists every missing, extra or differing value (numbers within 1e-3, relative above 1;
text exactly). The exit status is the number of scenarios with a difference that is not listed below,
0 when all others match; `--scenario PREFIX` limits the comparison (and the Unbound run,
NAVPATH_PROBE_ONLY) to scenarios whose name starts with it.

The optional `--profile layers` runs the stacked-link diagnostic. It has its own canonical
Reference/unity-<release>/layers.json and nested capture directory. Every difference fails;
there are no listed keys for this profile. Its currently failing scope is in README.md.

The `--profile obstacle-separation` records close-range steering and capsule contact. It uses
Reference/unity-<release>/obstacle-separation.json and compares all values at the same tolerance.

Listed, not gated (docs/COMPATIBILITY_GAPS.md, the path-cost row):
  hole.shapes two.p1.*    a straight line grazing a mesh vertex that the builders place about 2e-6
                          apart, on either side of the line: Unity keeps the corner there, Unbound
                          does not.
  carve.rules poly.p47, poly.p51
                          two pieces where the capsule round the box at (-3, 11) cuts a piece the
                          lying capsule left: Unity joins the fan triangle under it to the piece
                          on its right, Unbound to the one on its left. Which is joined depends on
                          the vertex the cut piece starts at, which follows from where the built
                          NavMesh tile's polygon starts; Unity's starts at another corner in some
                          tiles (docs/COMPATIBILITY_GAPS.md, the carving row).
  place.links m.p.ab, r30.p.ab, r30.w.ab, m.w.ba
                          the route past a link's landing on the second NavMesh: Unity's bends at a
                          tile corner there (moved or turned alike), Unbound's runs straight; over a
                          wide end lying over two tiles, Unity's crosses the part over the other tile
                          (docs/COMPATIBILITY_GAPS.md, the placed NavMesh data row);
  link.wide gb1.2.cur.*   an agent reaching a wide link at a NavMesh edge has approach and crossing
                          offsets differing by 16.7 and 4.6 cm in the current capture
                          (docs/COMPATIBILITY_GAPS.md, the link rows).
  replan.topofar a.given, mask.given
                          retained listed keys, currently matching: the runtime raster-bounds
                          correction resolves both corner counts; all 1687 values match;
"""
import argparse
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import project_copy, reference_file, reference_root, run_root, unity_version
from editor_runner import EditorJob, load_result, result_problem, run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPEATS = ('native1', 'native2')
OUTPUT = 'navpath.json'
TOLERANCE = 1e-3
FRAMES = 5000
OBSTACLE_SEPARATION = 'obstacle-separation'
# Scenario -> key prefixes of the differences that are reported but not gated ('' = all of them).
LISTED = {
    'hole.shapes': ('two.p1.',),
    'link.wide': ('gb1.2.cur.start.z', 'gb1.2.cur.end.z', 'gb1.2.cur.at.z'),
    'carve.rules': ('poly.p47', 'poly.p51'),
    'place.links': tuple(f'{path}.{part}' for path in ('m.p.ab', 'r30.p.ab', 'r30.w.ab', 'm.w.ba') for part in ('n', 'c')),
    'replan.topofar': ('a.given', 'mask.given'),
}


def profile_reference(args):
    return reference_file(PROBE) if args.profile == 'main' else reference_root(PROBE)/f'{args.profile}.json'


def capture_root(args):
    root = run_root(PROBE, args.label)
    return root if args.profile == 'main' else root/args.profile


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    return dest


def select_project_release(project, args):
    if args.profile == OBSTACLE_SEPARATION:
        (project/'ProjectSettings/ProjectVersion.txt').write_text(
            f'm_EditorVersion: {unity_version()}\n')


def editor(args):
    root = capture_root(args)
    jobs = []
    for name in REPEATS:
        out = root/name
        if out.exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        project = copy_project(out/'project')
        select_project_release(project, args)
        jobs.append(EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'], batch=True,
                              env={'NAVPATH_PROBE_OUT': str(out/OUTPUT), 'NAVPATH_PROBE_ONLY': args.scenario, 'NAVPATH_PROBE_PROFILE': args.profile}, timeout=1200, label='navpath-'+name))
    failed = False
    for result, name in zip(run_parallel(jobs), REPEATS):
        print(summary(result))
        problem = capture_problem(root/name, args)
        if problem or not (root/name/OUTPUT).exists():
            print(f'{name}: {problem or "no output"}')
            failed = True
    return 1 if failed else 0


def capture_problem(directory, args):
    if args.profile != OBSTACLE_SEPARATION:
        return result_problem(directory)
    problem = result_problem(directory, gpu=True)
    if problem:
        return problem
    result = load_result(directory)
    if result.get('forced_exit') or result.get('exit_code') != 0:
        return 'the obstacle separation Editor must exit normally'
    if Path(result.get('project', '')).resolve() != (directory/'project').resolve():
        return 'the Editor ran a different copy of the project'
    return obstacle_separation_problem(directory/OUTPUT)


def obstacle_separation_problem(path):
    def unique_object(pairs):
        record = dict(pairs)
        if len(record) != len(pairs):
            raise ValueError('duplicate keys in the obstacle separation capture')
        return record

    try:
        document = json.loads(path.read_bytes(), object_pairs_hook=unique_object)
    except (OSError, ValueError) as error:
        return str(error)
    if not isinstance(document, dict) or set(document) != {'steer.separation'}:
        return 'expected the obstacle separation scenario'
    records = document['steer.separation']
    if not isinstance(records, dict) or records.get('release') != unity_version():
        return 'wrong Unity release in the obstacle separation capture'
    numeric = set()
    statuses = set()
    pending = set()
    for case in ('clear', 'post', 'farther', 'neighbour', 'touch', 'passthrough'):
        for frame in (1, 2, 3, 4, 5, 10, 20, 30):
            key = f'{case}.f{frame}'
            numeric.update(f'{key}.{vector}.{axis}' for vector in ('at', 'desired', 'velocity')
                           for axis in 'xyz')
            statuses.add(f'{key}.pathStatus')
            pending.add(f'{key}.pathPending')
    for case in ('shaft', 'lower-cap', 'upper-cap', 'above', 'tall', 'zero-speed',
                 'slow-speed', 'zero-foot', 'short', 'on-axis', 'on-axis-path',
                 'lower-cap-path', 'above-path', 'cap-graze', 'near-axis-left', 'near-axis-right'):
        numeric.update(f'{case}.f{frame}.at.{axis}' for frame in (1, 3) for axis in 'xyz')
    for case in ('on-axis-path', 'lower-cap-path', 'above-path', 'cap-graze'):
        numeric.update(f'{case}.f{frame}.desired.{axis}' for frame in (1, 3) for axis in 'xyz')
    for case in ('scaled-cap-interior', 'cap-interior', 'cap-surface-near', 'cap-surface-far'):
        numeric.update(f'{case}.f{frame}.at.{axis}' for frame in (1, 2, 3) for axis in 'xyz')
    expected = numeric | statuses | pending | {'release', 'mesh0.triangles', 'mesh0.vertices'}
    if set(records) != expected:
        return (f'missing separation keys {sorted(expected - records.keys())}, '
                f'unexpected keys {sorted(records.keys() - expected)}')
    for key in sorted(numeric):
        value = records[key]
        if type(value) not in (float, int) or not math.isfinite(value):
            return f'{key} is not a finite number'
    for key in sorted(statuses):
        if records[key] != 'PathComplete':
            return f'{key} did not record a complete route'
    for key in sorted(pending):
        if type(records[key]) is not bool:
            return f'{key} is not a boolean'
    if type(records['mesh0.triangles']) is not int or records['mesh0.triangles'] <= 0:
        return 'capture has no NavMesh triangles'
    if type(records['mesh0.vertices']) is not str or not records['mesh0.vertices']:
        return 'capture has no NavMesh vertices'
    return None


def publish(args):
    root = capture_root(args)
    for name in REPEATS:
        problem = capture_problem(root/name, args)
        if problem:
            sys.exit(f'{name}: {problem}')
    first, second = ((root/name/OUTPUT).read_bytes() for name in REPEATS)
    if first != second:
        sys.exit('the two captures differ; nothing published')
    target = profile_reference(args)
    if args.profile == OBSTACLE_SEPARATION and target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(first)
    print(f'published {target}')
    return 0


def same(a, b):
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        return abs(a - b) <= TOLERANCE*max(1.0, abs(a))
    return a == b


def compare(reference, candidate, prefix, details):
    """Report differences, with an optional per-scenario detail limit."""
    bad = 0
    for scenario, values in reference.items():
        if not scenario.startswith(prefix):
            continue
        other = candidate.get(scenario)
        if other is None:
            print(f'{scenario}: missing from the candidate')
            if details is None:
                for key, value in values.items():
                    print(f'  {key}: unity {value!r}  unbound "<missing>"')
            bad += 1
            continue
        keys = [k for k, v in values.items() if not same(v, other.get(k, '<missing>'))]
        keys += [k for k in other if k not in values]
        lines = [f'  {k}: unity {values.get(k, "<missing>")!r}  unbound {other.get(k, "<missing>")!r}' for k in keys]
        listed = [k for k in keys if any(k.startswith(p) for p in LISTED.get(scenario, ()))]
        note = f' ({len(listed)} listed, not gated)' if listed else ''
        print(f'{scenario}: ' + (f'{len(keys)} of {len(values)} values differ{note}' if keys else f'all {len(values)} values match'))
        for line in lines[:details]:
            print(line)
        if details is not None and len(lines) > details:
            print(f'  ... {len(lines) - details} more')
        bad += len(keys) > len(listed)
    for scenario in candidate:
        if scenario.startswith(prefix) and scenario not in reference:
            print(f'{scenario}: not in the reference')
            if details is None:
                for key, value in candidate[scenario].items():
                    print(f'  {key}: unity "<missing>"  unbound {value!r}')
            bad += 1
    return bad


def unbound(args):
    binary = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI')
    if not binary:
        sys.exit('set UNBOUND to the unbound binary')
    out = capture_root(args)/'unbound'
    out.mkdir(parents=True, exist_ok=True)
    result = out/OUTPUT
    result.unlink(missing_ok=True)
    launcher = Path.home()/'.cache/compat/with-heavy-job'
    project = project_copy(PROBE, out/'project', fresh=True)
    select_project_release(project, args)
    command = [binary, 'run', '-p', str(project), '--no-watch', '--frames', str(FRAMES)]
    if launcher.exists():
        command = [str(launcher), '--kind', 'engine-cpu', *command]
    env = dict(os.environ, NAVPATH_PROBE_OUT=str(result), NAVPATH_PROBE_ONLY=args.scenario, NAVPATH_PROBE_PROFILE=args.profile)
    with open(out/'run.log', 'w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=900).returncode
    if code != 0 or not result.exists():
        sys.exit(f'Unbound failed (exit {code}); see {out/"run.log"}')
    details = None if args.profile == OBSTACLE_SEPARATION else args.details
    bad = compare(json.loads(profile_reference(args).read_text()), json.loads(result.read_text()), args.scenario, details)
    print(f'gated: {bad} scenario(s) differ')
    return min(bad, 100)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--label', default='current')
    parser.add_argument('--profile', choices=('main', 'layers', OBSTACLE_SEPARATION), default='main',
                        help='main regression set, stacked links or obstacle separation')
    parser.add_argument('--scenario', default='')
    parser.add_argument('--details', type=int, default=6, help='detail limit for the main and layers profiles')
    args = parser.parse_args()
    if args.profile != 'main' and args.scenario:
        parser.error('--scenario is supported only by the main profile')
    print(f'Unity {unity_version()}', file=sys.stderr)
    return {'editor': editor, 'publish': publish, 'unbound': unbound}[args.command](args)


if __name__ == '__main__':
    sys.exit(main())
