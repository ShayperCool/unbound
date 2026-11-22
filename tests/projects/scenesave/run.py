#!/usr/bin/env python3
"""Scene and asset saves, compared with the Unity Editor's files.

Each case runs the probe's tests (Unity Test Framework) on a fresh copy of the project, in the
Editor (`editor`) or in Unbound (`unbound`), and keeps the rows the tests log and every project
file the run added, changed or removed (Library, Logs, Temp and UserSettings aside). `compare`
checks a capture against `Reference/unity-<release>/` (or another capture); `gate` runs Unbound and
compares it with the reference.

Cases: `edit` (Edit Mode SaveEditTests), `play-nosave` and `play-save` (Play Mode SavePlayTests
NoSave and Save, each alone), `prefab-api` (Edit Mode PrefabApiTests), `prefab-writes` (Edit Mode
PrefabWriteTests), `create-assets` (Edit Mode CreateAssetTests), `engine-assets` (Edit Mode
EngineAssetTests), `prefab-nested` (Edit Mode NestedPrefabTests), `settings-writes` (Edit Mode
SettingsWriteTests), `mesh-bounds` (Edit Mode MeshBoundsTests), `native-saves` (Edit Mode
NativeSaveTests), `clip-assets` (Edit Mode ClipAssetTests), `controller-assets` (Edit Mode
ControllerAssetTests), `modification-timing` (Edit Mode ModificationTimingTests), `scene-api-rest`
(Edit Mode SceneApiRestTests).
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/"tests"))
sys.path.insert(0, str(PROBE))
from probe_paths import editor_path, reference_root, run_root  # noqa: E402
import scenesave_compare  # noqa: E402

CASES = {
    'edit': ('EditMode', 'SaveEditTests.SaveFlow'),
    'play-nosave': ('PlayMode', 'SavePlayTests.NoSave'),
    'play-save': ('PlayMode', 'SavePlayTests.Save'),
    'prefab-api': ('EditMode', 'PrefabApiTests.PrefabApi'),
    'prefab-writes': ('EditMode', 'PrefabWriteTests.PrefabWrites'),
    'create-assets': ('EditMode', 'CreateAssetTests.CreateAssets'),
    'engine-assets': ('EditMode', 'EngineAssetTests.EngineAssets'),
    'prefab-nested': ('EditMode', 'NestedPrefabTests.NestedPrefabs'),
    'settings-writes': ('EditMode', 'SettingsWriteTests.SettingsWrites'),
    'mesh-bounds': ('EditMode', 'MeshBoundsTests.MeshBounds'),
    'native-saves': ('EditMode', 'NativeSaveTests.NativeSaves'),
    'clip-assets': ('EditMode', 'ClipAssetTests.ClipAssets'),
    'controller-assets': ('EditMode', 'ControllerAssetTests.ControllerAssets'),
    'modification-timing': ('EditMode', 'ModificationTimingTests.ModificationTiming'),
    'scene-api-rest': ('EditMode', 'SceneApiRestTests.SceneApiRest'),
}
SKIPPED = {'Library', 'Logs', 'Temp', 'UserSettings', 'obj', 'run.py', 'README.md', 'Reference',
           'gate.json', 'scenesave_compare.py', 'test_compare.py', '__pycache__'}


def project_files(root):
    files = {}
    for path in sorted(root.rglob('*')):
        rel = path.relative_to(root)
        if rel.parts[0] in SKIPPED or not path.is_file():
            continue
        files[rel.as_posix()] = path.read_bytes()
    return files


def copy_project(destination):
    """The probe's authored files (no Library: each run imports the project afresh)."""
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        source = PROBE/name
        if source.exists():
            shutil.copytree(source, destination/name, symlinks=True)


def run_case(kind, case, out, cli=None):
    platform, test_filter = CASES[case]
    scratch = run_root(PROBE, 'work')
    scratch.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix=f'{case}-', dir=scratch))
    project = work/'scenesave'
    copy_project(project)
    rows = work/'rows.jsonl'
    env = dict(os.environ, SCENESAVE_LOG=str(rows))
    results = work/'results.xml'
    log = work/'run.log'
    if kind == 'editor':
        command = [str(editor_path()), '-batchmode', '-nographics', '-accept-apiupdate', '-projectPath', str(project),
                   '-runTests', '-testPlatform', platform, '-testFilter', test_filter,
                   '-testResults', str(results), '-logFile', str(log)]
    else:
        command = [cli or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound'), 'test', '-p', str(project),
                   '--platform', platform, '--filter', test_filter, '--results', str(results)]
    with open(log, 'ab') as output:
        code = subprocess.run(command, env=env, stdout=output, stderr=subprocess.STDOUT, timeout=1800).returncode
    before = project_files(PROBE)
    after = project_files(project)
    if out.exists():
        shutil.rmtree(out)
    (out/'files').mkdir(parents=True)
    changed = []
    for rel, data in after.items():
        if before.get(rel) != data:
            target = out/'files'/rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            changed.append(rel)
    removed = [rel for rel in before if rel not in after]
    summary = {'case': case, 'exit': code, 'changed': changed, 'removed': removed}
    (out/'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    shutil.copy(rows, out/'rows.jsonl') if rows.exists() else (out/'rows.jsonl').write_text('')
    if (work/'snapshots').is_dir():
        shutil.copytree(work/'snapshots', out/'snapshots')
    if results.exists():
        shutil.copy(results, out/'results.xml')
    shutil.copy(log, out/'run.log')
    shutil.rmtree(work, ignore_errors=True)
    print(f'{case}: exit {code}, {len(changed)} files written, {len(removed)} removed -> {out}')
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    for name in ('editor', 'unbound'):
        p = sub.add_parser(name)
        p.add_argument('--case', choices=sorted(CASES), action='append')
        p.add_argument('--out', type=Path)
        p.add_argument('--release')
    c = sub.add_parser('compare')
    c.add_argument('reference', type=Path)
    c.add_argument('actual', type=Path)
    c.add_argument('--case', choices=sorted(CASES), action='append')
    f = sub.add_parser('compare-file')
    f.add_argument('reference', type=Path)
    f.add_argument('actual', type=Path)
    g = sub.add_parser('gate')
    g.add_argument('--release')
    args = parser.parse_args()
    if getattr(args, 'release', None):
        os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.command in ('editor', 'unbound'):
        out = args.out or run_root(PROBE, args.command)
        for case in args.case or sorted(CASES):
            run_case(args.command, case, out/case)
        return 0
    if args.command == 'compare':
        return scenesave_compare.compare_runs(args.reference, args.actual, args.case or sorted(CASES))
    if args.command == 'compare-file':
        problem = scenesave_compare.compare_file(args.reference, args.actual, args.actual.name)
        print(problem or 'identical')
        return int(problem is not None)
    out = run_root(PROBE, 'gate')
    for case in sorted(CASES):
        run_case('unbound', case, out/case)
    return scenesave_compare.compare_runs(reference_root(PROBE), out, sorted(CASES), report=out/'report.json')


if __name__ == '__main__':
    sys.exit(main())
