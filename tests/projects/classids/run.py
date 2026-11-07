#!/usr/bin/env python3
"""Record Unity's class table (native), publish it, check `unbound doctor` against it.

    run.py native --label native1         # a private project copy, GPU Editor through the job pool
    run.py publish native1 native2         # two agreeing captures become Reference/unity-<version>.jsonl
    run.py check                           # `unbound doctor --all --json` on this project (the landing gate)

Run output stays under $XDG_CACHE_HOME/unbound/validation/runs/classids/.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, run_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, target/folder)


def native(label):
    out = WORK/label
    project = out/'project'
    copy_project(project)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ClassTable.Record'],
                                  batch=True, graphics=True, env={'CLASS_TABLE_OUT': str(out/OUT)},
                                  timeout=1200, label='classids-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    return 0 if (out/OUT).exists() else 1


def publish(labels):
    target = reference_file(ROOT, 'jsonl')

    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    return editor_runner.publish([WORK/label for label in labels], target, lambda d: [OUT], write,
                                 complete=[editor_runner.jsonl_complete(OUT)])


def check():
    """The doctor reads the release's table: every class it lists as unread in the release is a
    concrete non-editor class of the reference, and the project's own classes are all read."""
    out = WORK/(time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid()))
    out.mkdir(parents=True)
    project = out/'project'
    copy_project(project)
    (project/'Assets/legacy.unity').write_text(legacy_scene())
    (project/'Assets/legacy.unity.meta').write_text('fileFormatVersion: 2\nguid: 9b742a2ce5674784b9ee27b437d7c503\nDefaultImporter:\n  externalObjects: {}\n')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    command = [str(HEAVY), '--kind', 'engine-cpu', 'timeout', '600', cli, 'doctor', '--all', '--json', '-p', str(project)]
    with (out/'doctor.json').open('w') as stdout, (out/'doctor.log').open('w') as stderr:
        code = subprocess.run(command, stdout=stdout, stderr=stderr).returncode
    if code != 0:
        print(f'doctor exit {code}; see {out}/doctor.log')
        return 1
    report = json.loads((out/'doctor.json').read_text())
    table = {}
    for line in reference_file(ROOT, 'jsonl').read_text().splitlines():
        row = json.loads(line)
        if 'id' in row:
            table[row['id']] = row
    problems = []
    if report.get('release') != unity_version():
        problems.append(f"release {report.get('release')} is not {unity_version()}")
    if not report.get('hasTable'):
        problems.append('the doctor has no class table for the release')
    unread = report.get('unreadInRelease', [])
    for entry in unread:
        row = table.get(entry['classId'])
        if row is None or row['abstract'] or row['editorOnly'] or row['name'] != entry['unityName']:
            problems.append(f"listed as unread but not a concrete runtime class: {entry}")
    for entry in report.get('classes', []):
        if entry['class']['reading'] not in ('Read', 'Loader', 'Ignored'):
            problems.append(f"the probe's own class is not read: {entry['class']}")
    by_id = {entry['class']['classId']: entry['class'] for entry in report.get('classes', [])}
    for class_id, reading in ((81, 'Read'), (124, 'Read'), (92, 'Ignored')):
        if by_id.get(class_id, {}).get('reading') != reading:
            problems.append(f'component {class_id} expected {reading}: {by_id.get(class_id)}')
    if any(e['classId'] in (81, 124) for e in unread):
        problems.append('enabled-only component listed without a reader')
    legacy_ref = ROOT/'Reference'/('unity-' + unity_version() + '-legacy.txt')
    expected = {'edit.legacy=UnityEngine.Transform', 'play.legacy=UnityEngine.Transform',
                'play.listener=UnityEngine.Transform,UnityEngine.AudioListener:false'}
    if not legacy_ref.exists() or not expected.issubset(set(legacy_ref.read_text().splitlines())):
        problems.append('legacy component reference missing or unexpected')
    for problem in problems:
        print(problem)
    print(f"{len(unread)} release classes without a reader; {len(problems)} problems")
    return 1 if problems else 0


def legacy_scene():
    scene = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
    for index, (label_name, ids) in enumerate((('legacy', (92,)), ('listener', (81,)), ('flare', (124,)), ('combined', (81, 92, 124)))):
        base = 1000 * (index + 1)
        scene += f"--- !u!1 &{base}\nGameObject:\n  serializedVersion: 6\n  m_Component:\n  - component: {{fileID: {base+4}}}\n"
        for class_id in ids:
            scene += f"  - component: {{fileID: {base+class_id}}}\n"
        scene += f"  m_Name: {label_name}\n  m_IsActive: 1\n"
        for class_id in (4, *ids):
            name = {4: 'Transform', 81: 'AudioListener', 92: 'GUILayer', 124: 'FlareLayer'}[class_id]
            scene += f"--- !u!{class_id} &{base+class_id}\n{name}:\n  m_GameObject: {{fileID: {base}}}\n"
            if class_id == 4:
                scene += "  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalScale: {x: 1, y: 1, z: 1}\n  m_Father: {fileID: 0}\n"
            else:
                scene += "  m_Enabled: 0\n"
    return scene


def legacy(label):
    out = WORK/label
    project = out/'project'
    copy_project(project)
    shutil.copy2(ROOT/'LegacyComponents.cs', project/'Assets/Editor/LegacyComponents.cs')
    (project/'Assets/legacy.unity').write_text(legacy_scene())
    (project/'Assets/legacy.unity.meta').write_text('fileFormatVersion: 2\nguid: 9b742a2ce5674784b9ee27b437d7c503\nDefaultImporter:\n  externalObjects: {}\n')
    output = out/'legacy.txt'
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'LegacyComponents.Run'],
                                  batch=True, graphics=True, env={'LEGACY_COMPONENTS_OUT': str(output)},
                                  timeout=1200, label='classids-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    return 0 if output.exists() and 'play.legacy=' in output.read_text() else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'publish', 'check', 'legacy', 'publish-legacy'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    if args.mode == 'publish-legacy':
        target = ROOT/'Reference'/('unity-' + unity_version() + '-legacy.txt')
        def write(path, first):
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(first/'legacy.txt', path)
        return editor_runner.publish([WORK/label for label in args.labels], target, lambda d: ['legacy.txt'], write)
    if args.mode == 'legacy':
        return legacy(args.label)
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    return check()


if __name__ == '__main__':
    sys.exit(main())
