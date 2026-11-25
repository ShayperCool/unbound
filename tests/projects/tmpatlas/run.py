#!/usr/bin/env python3
"""Texture and TextMeshPro font asset writes of a Play mode session, and the quit callbacks (see
README.md).

    run.py create              # once, with the macOS Editor: makes Assets/Tex and Assets/Fonts
    run.py native --label L    # a capture of each profile with the macOS Editor in batch mode
                               # (the caller holds the machine's heavy-job lock)
    run.py publish L1 L2       # two byte-identical captures -> Reference/unity-<release>/<profile>.jsonl
    run.py check               # Unbound on fresh copies against the references

The profiles are the project as it is (Gamma colour space) and with the Linear colour space.
Every run works on a fresh copy of the probe's project with TextMeshPro's Essential Resources
(its shaders and settings) added from the Unity installation's ugui package, as the Editor's
importer window adds them. The probe's rows say which assets are dirty after each change and the
order of the quit callbacks; after the process ends this driver adds `exit.<path>` for every file
of the project but the unchanged Essential Resources (tests/file_writes.py).
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from file_writes import PARTS, exit_rows, files  # noqa: E402
from probe_paths import editor_path, reference_root, run_root  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
RAW = 'probe.jsonl'
VARIABLE = 'TMP_ATLAS_OUT'
COMPLETE = {'key': 'complete', 'value': '1'}
ESSENTIALS = 'Assets/TextMesh Pro/'
FILLED = 'Assets/Fonts/Filled SDF.asset'
# The project's colour space in each profile (ProjectSettings m_ActiveColorSpace).
PROFILES = {'Gamma': 0, 'Linear': 1}


def essentials_package():
    """TextMeshPro's Essential Resources in the ugui package of the Unity installation."""
    editor = editor_path()
    data = editor.parents[1]/'Resources' if sys.platform == 'darwin' else editor.parent/'Data'/'Resources'
    package = data/'PackageManager/BuiltInPackages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage'
    if not package.is_file():
        raise SystemExit(f'no TextMeshPro Essential Resources at {package}')
    return package


def add_essentials(project):
    """Unpacks the Essential Resources into the project: each entry's asset and .meta at its path."""
    entries = {}
    with tarfile.open(essentials_package(), 'r:gz') as archive:
        for member in archive.getmembers():
            if not member.isfile():
                continue
            guid, _, name = member.name.lstrip('./').partition('/')
            entries.setdefault(guid, {})[name] = archive.extractfile(member).read()
    for entry in entries.values():
        path = entry['pathname'].decode().splitlines()[0].strip()
        target = project/path
        if 'asset' in entry:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(entry['asset'])
        else:
            target.mkdir(parents=True, exist_ok=True)
        if 'asset.meta' in entry:
            Path(str(target) + '.meta').write_bytes(entry['asset.meta'])


def project_at(out, profile='Gamma'):
    project = out/'project'
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    for folder in PARTS:
        shutil.copytree(ROOT/folder, project/folder)
    settings = project/'ProjectSettings/ProjectSettings.asset'
    text = settings.read_text()
    assert '  m_ActiveColorSpace: 0\n' in text
    settings.write_text(text.replace('  m_ActiveColorSpace: 0\n', f'  m_ActiveColorSpace: {PROFILES[profile]}\n', 1))
    add_essentials(project)
    return project


def run_editor(project, out, method, env=None):
    editor = editor_path()
    with (out/'run.out').open('a') as log:
        return subprocess.run(['timeout', '-s', 'KILL', '1500', str(editor), '-batchmode', '-projectPath', str(project),
                               '-executeMethod', method, '-logFile', str(out/(method.split('.')[-1] + '.log'))],
                              env=dict(os.environ, **(env or {})), stdout=log, stderr=subprocess.STDOUT).returncode


def create():
    """Makes the texture and font assets with the Editor and copies them into the probe; Filled's
    clearing is turned on in its file (it was made with clearing off, so that its characters stay)."""
    if sys.platform != 'darwin':
        raise SystemExit('tmpatlas authors its assets with the macOS Editor')
    out = WORK/time.strftime('create-%Y%m%d-%H%M%S')
    project = project_at(out)
    code = run_editor(project, out, 'TmpAtlasProbe.Create')
    print('create: Editor exit', code)
    if code:
        return code
    for path in sorted(project.rglob('*')):
        relative = path.relative_to(project).as_posix()
        if path.is_file() and not (relative + '/').startswith(ESSENTIALS) and not relative.startswith(ESSENTIALS[:-1] + '.meta') \
                and relative.split('/')[0] in PARTS:
            target = ROOT/relative
            if not target.exists() or target.read_bytes() != path.read_bytes():
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target)
                print('wrote', relative)
    filled = ROOT/FILLED
    text = filled.read_text()
    assert '  m_ClearDynamicDataOnBuild: 0\n' in text
    filled.write_text(text.replace('  m_ClearDynamicDataOnBuild: 0\n', '  m_ClearDynamicDataOnBuild: 1\n', 1))
    return 0


def finish(out, project, initial):
    """values.jsonl: the probe's rows, then what the run left on disk, then the completion marker."""
    raw = out/RAW
    rows = [json.loads(line) for line in raw.read_text().splitlines() if line.strip()] if raw.is_file() else []
    complete = COMPLETE in rows
    written = [row for row in exit_rows(project, initial)
               if not (row['key'].startswith('exit.' + ESSENTIALS[:-1]) and row['value'] == 'unchanged')]
    rows = sorted((row for row in rows if row != COMPLETE), key=lambda row: row['key']) + written
    if complete:
        rows.append(COMPLETE)
    (out/OUT).write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in rows))
    return complete


def native(label):
    if sys.platform != 'darwin':
        raise SystemExit('tmpatlas records with the macOS Editor in batch mode')
    failed = 0
    for profile in PROFILES:
        out = WORK/label/profile
        project = project_at(out, profile)
        initial = files(project)
        code = run_editor(project, out, 'TmpAtlasProbe.Record', {VARIABLE: str(out/RAW)})
        complete = finish(out, project, initial)
        print(label, profile, 'Editor exit', code, 'complete', complete)
        failed |= int(code != 0 or not complete)
    return failed


def engine(label, profile):
    out = WORK/label/profile
    project = project_at(out, profile)
    initial = files(project)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, **{VARIABLE: str(out/RAW)})
    command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '600', cli, 'run', '-p', str(project),
               '--no-watch', '--headless', '600']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    complete = finish(out, project, initial)
    print('engine exit', code, 'complete', complete, 'log', out/'engine.log')
    return int(code != 0 or not complete)


def rows(path):
    result = {}
    for line in path.read_text().splitlines():
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError(f'duplicate key {row["key"]} in {path}')
        result[row['key']] = row['value']
    if result.get('complete') != '1':
        raise ValueError(f'incomplete capture {path}')
    return result


def publish(labels):
    if len(labels) < 2:
        sys.exit('publishing needs two independent repeats')
    for profile in PROFILES:
        captures = [(WORK/label/profile/OUT).read_bytes() for label in labels]
        for label in labels:
            rows(WORK/label/profile/OUT)
        if any(capture != captures[0] for capture in captures):
            sys.exit(f'the {profile} repeats differ; not publishing')
    for profile in PROFILES:
        target = reference_root(ROOT)/(profile + '.jsonl')
        if target.exists():
            sys.exit(f'refusing to overwrite reference: {target}')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((WORK/labels[0]/profile/OUT).read_bytes())
        print('published', target)
    return 0


def residuals():
    path = ROOT/'residuals.json'
    return {key: reason for reason, keys in json.loads(path.read_text()).items() for key in keys} if path.is_file() else {}


def compare(candidates):
    """Every row of each profile exactly; rows residuals.json names (in every profile) are printed
    with their cause and do not fail. `candidates`: {profile: values.jsonl}."""
    known = residuals()
    total = matched = failed = residual = 0
    for profile, candidate in candidates.items():
        expected, actual = rows(reference_root(ROOT)/(profile + '.jsonl')), rows(candidate)
        for key in sorted(expected.keys() | actual.keys()):
            if key in expected and key in actual and expected[key] == actual[key]:
                if key in known:
                    print('residual resolved (remove it from residuals.json):', profile, key)
                continue
            if key in known:
                residual += 1
                print('RESIDUAL', profile, key, '-', known[key])
                continue
            failed += 1
            print('FAIL', profile, key, 'expected', json.dumps(expected.get(key)), 'actual', json.dumps(actual.get(key)))
        total += len(expected)
        matched += sum(key in actual and actual[key] == value for key, value in expected.items())
    print(f'tmpatlas: {matched}/{total} rows match; {failed} differ; {residual} known residuals')
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['create', 'native', 'engine', 'publish', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path, help='a run directory holding <profile>/values.jsonl')
    args = parser.parse_args()
    labels = args.labels + ([args.label] if args.label else [])
    if any(Path(label).name != label or label in ('.', '..') for label in labels):
        parser.error('labels must be single directory names')
    if args.mode in ('native', 'engine') and not args.label:
        parser.error('--label is required for native and engine runs')
    if args.mode == 'create':
        return create()
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return max(engine(args.label, profile) for profile in PROFILES)
    if args.mode == 'publish':
        return publish(args.labels)
    if args.mode == 'compare':
        return compare({profile: args.candidate/profile/OUT for profile in PROFILES})
    label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
    code = max(engine(label, profile) for profile in PROFILES)
    return code or compare({profile: WORK/label/profile/OUT for profile in PROFILES})


if __name__ == '__main__':
    sys.exit(main())
