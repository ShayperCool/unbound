#!/usr/bin/env python3
"""Audit committed binary bundle scripts and an independent unread-field control."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import run_root, unity_version  # noqa: E402


def main():
    fixture = ROOT.parent/'assetbundles'
    out = run_root(ROOT, 'fields')/(time.strftime('%Y%m%d-%H%M%S-')+str(os.getpid()))
    project = out/'project'
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(fixture/part, project/part)
    bundle = project/'Assets/DoctorBundle.bundle'
    shutil.copy2(fixture/'Reference/bundles/none/things', bundle)
    meta = 'fileFormatVersion: 2\nguid: {}\nDefaultImporter:\n  externalObjects: {{}}\n'
    bundle.with_suffix('.bundle.meta').write_text(meta.format('5dd0a24d42d74ec0b5578231fdcab217'))
    control = project/'Assets/CoverageControl.prefab'
    control.write_text('%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: control\n  m_Component:\n  - component: {fileID: 4}\n--- !u!4 &4\nTransform:\n  m_GameObject: {fileID: 1}\n  doctorUnknownControl: 0\n  m_EditorHideFlags: 0\n')
    control.with_suffix('.prefab.meta').write_text(meta.format('5930f18ef0614ba3a17eacfa090b1593'))
    texture = project/'Assets/DoctorTexture.png'
    shutil.copy2(ROOT.parent/'flares/Assets/Resources/Uv.png', texture)
    texture.with_suffix('.png.meta').write_text('fileFormatVersion: 2\nguid: 05770d8bc6a958fc2f2d162a2f819175\nTextureImporter:\n  mipmaps:\n    enableMipMap: 0\n  doctorUnknownImporter: 0\n')
    (project/'ProjectSettings/DoctorPackageSettings.asset').write_text('{"doctorUnknownJsonSettings":0}')
    settings = project/'ProjectSettings/ProjectSettings.asset'
    with settings.open('a') as stream:
        stream.write('  doctorUnknownSettings: 0\n')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-cpu',
               'timeout', '600', cli, 'doctor', '--all', '--json', '-p', str(project)]
    with (out/'doctor.json').open('w') as stdout, (out/'doctor.log').open('w') as stderr:
        subprocess.run(command, stdout=stdout, stderr=stderr, check=True)
    report = json.loads((out/'doctor.json').read_text())
    fields = report['serializedFields']
    problems = []
    if report['release'] != unity_version() or report['messageAnalysisFailedAssemblies']:
        problems.append('release or script compilation mismatch')
    if fields['incomplete']:
        problems.extend(fields['incomplete'])
    scripts = next(entry for entry in report['classes'] if entry['class']['classId'] == 114)
    if 'Assets/DoctorBundle.bundle' not in scripts['files'] or scripts['objects'] != 4:
        problems.append('expected two authored scripts and two binary bundle scripts')
    expected_read = {'level', 'title', 'range.x', 'tags[]', 'count', 'label', 'weights[]',
                     'points[].x', 'nested.name', 'nested.tint.r', 'data.fileID'}
    for field in fields['unreadMayChangeBehaviour']:
        if field['classId'] == 114 and field['path'] in expected_read:
            problems.append(field)
    if not any(f['classId'] == 4 and f['path'] == 'doctorUnknownControl' for f in fields['unreadMayChangeBehaviour']):
        problems.append('unknown zero-valued control was suppressed')
    if not any(f['classId'] == 4 and f['path'] == 'm_EditorHideFlags' and f.get('reason') for f in fields['knownNoRuntimeEffect']):
        problems.append('metadata control is missing its reviewed reason')
    for category, control in [('importerFields', 'TextureImporter.doctorUnknownImporter'),
                              ('projectSettingsFields', 'doctorUnknownSettings')]:
        section = report[category]
        problems.extend(section['incomplete'])
        if not any(field['path'] == control for field in section['unreadMayChangeBehaviour']):
            problems.append(category + ': unread control missing')
        if any(field['path'] in {'TextureImporter.mipmaps.enableMipMap', 'productName'}
               for field in section['unreadMayChangeBehaviour']):
            problems.append(category + ': actual reader consumption missing')
        if any(not field.get('reason', '').strip() for field in section['knownNoRuntimeEffect']):
            problems.append(category + ': metadata exception without reason')
    if not any(field['path'] == 'doctorUnknownJsonSettings' for field in report['projectSettingsFields']['unreadMayChangeBehaviour']):
        problems.append('JSON package settings field is missing')
    for problem in problems:
        print(problem)
    print(f"field coverage: {fields['objects']} objects, {len(problems)} problems; {out}")
    return bool(problems)


if __name__ == '__main__':
    raise SystemExit(main())
