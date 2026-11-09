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

Listed, not gated (docs/COMPATIBILITY_GAPS.md, the path-cost row):
  hole.shapes two.p1.*    a straight line grazing a mesh vertex that the builders place about 2e-6
                          apart, on either side of the line: Unity keeps the corner there, Unbound
                          does not.
  carve.shortcut          measured: Unity's agent keeps going round when a carved gap opens in
                          tiles its path does not cross; Unbound's path search over the 5.33 m
                          tiles takes another staircase of tiles (east, then north along the
                          wall) that crosses the gap's tile, so its agent is planned again and
                          takes the gap (docs/COMPATIBILITY_GAPS.md, the path-cost row);
  carve.rules poly.p47, poly.p51
                          two pieces where the capsule round the box at (-3, 11) cuts a piece the
                          lying capsule left: Unity joins the fan triangle under it to the piece
                          on its right, Unbound to the one on its left. Which is joined depends on
                          the vertex the cut piece starts at, which follows from where the built
                          NavMesh tile's polygon starts; Unity's starts at another corner in some
                          tiles (docs/COMPATIBILITY_GAPS.md, the carving row).
  place.moved *.walk.f200 to f300
                          an agent walking round a wall's end: Unity's takes a shorter way than its
                          first path between frames 150 and 200 (it re-plans the start of its path while
                          walking), Unbound's keeps its path; the same for every pose of the
                          NavMesh (docs/COMPATIBILITY_GAPS.md, the placed NavMesh data row);
  place.links m.p.ab, r30.p.ab, r30.w.ab, m.w.ba
                          the route past a link's landing on the second NavMesh: Unity's bends at a
                          tile corner there (moved or turned alike), Unbound's runs straight; over a
                          wide end lying over two tiles, Unity's crosses the part over the other tile
                          (docs/COMPATIBILITY_GAPS.md, the placed NavMesh data row);
  link.wide gb1.2.cur.*   an agent reaching a wide link where the NavMesh under the link's start
                          ends (z 6): Unity's reaches it 6 cm further from that end and crosses from
                          5.98 to 5.82, Unbound's straight across from where it reaches it, 5.80
                          (docs/COMPATIBILITY_GAPS.md, the link rows).
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
OUTPUT = 'navpath.json'
TOLERANCE = 1e-3
FRAMES = 4000
# Scenario -> key prefixes of the differences that are reported but not gated ('' = all of them).
LISTED = {
    'hole.shapes': ('two.p1.',),
    'link.wide': ('gb1.2.cur.start.z', 'gb1.2.cur.end.z', 'gb1.2.cur.at.z'),
    'carve.shortcut': ('',),
    'carve.rules': ('poly.p47', 'poly.p51'),
    'place.moved': tuple(f'{pose}.walk.f{frame}' for pose in ('o', 'm', 'r30', 'r90') for frame in (200, 250, 300)),
    'place.links': ('m.p.ab.', 'r30.p.ab.', 'r30.w.ab.', 'm.w.ba.'),
}


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
                              env={'NAVPATH_PROBE_OUT': str(out/OUTPUT), 'NAVPATH_PROBE_ONLY': args.scenario}, timeout=1200, label='navpath-'+name))
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
    out = run_root(PROBE, args.label)/'unbound'
    out.mkdir(parents=True, exist_ok=True)
    result = out/OUTPUT
    result.unlink(missing_ok=True)
    launcher = Path.home()/'.cache/compat/with-heavy-job'
    command = [binary, 'run', '-p', str(PROBE), '--no-watch', '--frames', str(FRAMES)]
    if launcher.exists():
        command = [str(launcher), '--kind', 'engine-cpu', *command]
    env = dict(os.environ, NAVPATH_PROBE_OUT=str(result), NAVPATH_PROBE_ONLY=args.scenario)
    with open(out/'run.log', 'w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=900).returncode
    if code != 0 or not result.exists():
        sys.exit(f'Unbound failed (exit {code}); see {out/"run.log"}')
    bad = compare(json.loads(reference_file(PROBE).read_text()), json.loads(result.read_text()), args.scenario, args.details)
    print(f'gated: {bad} scenario(s) differ')
    return min(bad, 100)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--label', default='current')
    parser.add_argument('--scenario', default='')
    parser.add_argument('--details', type=int, default=6)
    args = parser.parse_args()
    print(f'Unity {unity_version()}', file=sys.stderr)
    return {'editor': editor, 'publish': publish, 'unbound': unbound}[args.command](args)


if __name__ == '__main__':
    sys.exit(main())
