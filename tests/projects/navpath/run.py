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
  replan.none avoider.f100 to f150
                          an agent with avoidance passing a NavMeshObstacle capsule: Unity's runs
                          1.4 to 4.7 cm off Unbound's from frame 100, as it passes the obstacle
                          (docs/COMPATIBILITY_GAPS.md, the NavMeshObstacle row).
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file, reference_root, run_root, unity_version
from editor_runner import EditorJob, result_problem, run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPEATS = ('native1', 'native2')
OUTPUT = 'navpath.json'
TOLERANCE = 1e-3
FRAMES = 5000
# Scenario -> key prefixes of the differences that are reported but not gated ('' = all of them).
LISTED = {
    'hole.shapes': ('two.p1.',),
    'link.wide': ('gb1.2.cur.start.z', 'gb1.2.cur.end.z', 'gb1.2.cur.at.z'),
    'carve.rules': ('poly.p47', 'poly.p51'),
    'place.links': tuple(f'{path}.{part}' for path in ('m.p.ab', 'r30.p.ab', 'r30.w.ab', 'm.w.ba') for part in ('n', 'c')),
    'replan.topofar': ('a.given', 'mask.given'),
    'replan.none': tuple(f'avoider.f{frame}.' for frame in range(100, 151, 10)),
}


def profile_reference(args):
    return reference_file(PROBE) if args.profile == 'main' else reference_root(PROBE)/'layers.json'


def capture_root(args):
    root = run_root(PROBE, args.label)
    return root if args.profile == 'main' else root/'layers'


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    return dest


def editor(args):
    root = capture_root(args)
    jobs = []
    for name in REPEATS:
        out = root/name
        if out.exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        project = copy_project(out/'project')
        jobs.append(EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'], batch=True,
                              env={'NAVPATH_PROBE_OUT': str(out/OUTPUT), 'NAVPATH_PROBE_ONLY': args.scenario, 'NAVPATH_PROBE_PROFILE': args.profile}, timeout=1200, label='navpath-'+name))
    failed = False
    for result, name in zip(run_parallel(jobs), REPEATS):
        print(summary(result))
        problem = result_problem(root/name)
        if problem or not (root/name/OUTPUT).exists():
            print(f'{name}: {problem or "no output"}')
            failed = True
    return 1 if failed else 0


def publish(args):
    root = capture_root(args)
    for name in REPEATS:
        problem = result_problem(root/name)
        if problem:
            sys.exit(f'{name}: {problem}')
    first, second = ((root/name/OUTPUT).read_bytes() for name in REPEATS)
    if first != second:
        sys.exit('the two captures differ; nothing published')
    target = profile_reference(args)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(first)
    print(f'published {target}')
    return 0


def same(a, b):
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        return abs(a - b) <= TOLERANCE*max(1.0, abs(a))
    return a == b


def compare(reference, candidate, prefix, details):
    """Prints per scenario the values that match and the differences (at most `details` each);
    returns the number of scenarios with a difference that is not listed in LISTED."""
    bad = 0
    for scenario, values in reference.items():
        if not scenario.startswith(prefix):
            continue
        other = candidate.get(scenario)
        if other is None:
            print(f'{scenario}: missing from the candidate')
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
        if len(lines) > details:
            print(f'  ... {len(lines) - details} more')
        bad += len(keys) > len(listed)
    for scenario in candidate:
        if scenario.startswith(prefix) and scenario not in reference:
            print(f'{scenario}: not in the reference')
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
    command = [binary, 'run', '-p', str(PROBE), '--no-watch', '--frames', str(FRAMES)]
    if launcher.exists():
        command = [str(launcher), '--kind', 'engine-cpu', *command]
    env = dict(os.environ, NAVPATH_PROBE_OUT=str(result), NAVPATH_PROBE_ONLY=args.scenario, NAVPATH_PROBE_PROFILE=args.profile)
    with open(out/'run.log', 'w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=900).returncode
    if code != 0 or not result.exists():
        sys.exit(f'Unbound failed (exit {code}); see {out/"run.log"}')
    bad = compare(json.loads(profile_reference(args).read_text()), json.loads(result.read_text()), args.scenario, args.details)
    print(f'gated: {bad} scenario(s) differ')
    return min(bad, 100)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--label', default='current')
    parser.add_argument('--profile', choices=('main', 'layers'), default='main',
                        help='main regression set, or strict stacked-link diagnostic')
    parser.add_argument('--scenario', default='')
    parser.add_argument('--details', type=int, default=6)
    args = parser.parse_args()
    if args.profile == 'layers' and args.scenario:
        parser.error('--scenario is supported only by the main profile')
    print(f'Unity {unity_version()}', file=sys.stderr)
    return {'editor': editor, 'publish': publish, 'unbound': unbound}[args.command](args)


if __name__ == '__main__':
    sys.exit(main())
