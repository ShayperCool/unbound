#!/usr/bin/env python3
"""Record into independent cache-only project copies with an explicit Unity release."""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import editor_path, run_root
from artifact_paths import artifact_path

SOURCES = {'sweeps': ('CapsuleSweeps.cs', 'CAPSULE_SWEEPS_OUT'), 'contacts': ('ContactProbe.cs', 'CONTACT_OUT'),
           'controls': ('ContactControls.cs', 'CONTACT_CONTROLS_OUT'),
           'geometry': ('ContactGeometry.cs', 'CONTACT_GEOMETRY_OUT'),
           'capsule': ('CapsuleGeometry.cs', 'CAPSULE_GEOMETRY_OUT')}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=['native', 'engine'])
    p.add_argument('--suite', choices=SOURCES, default='contacts')
    p.add_argument('--unity-version', required=True)
    p.add_argument('--work', type=Path, default=run_root(ROOT))
    p.add_argument('--label', required=True)
    p.add_argument('--profile', type=int, choices=range(4), default=0)
    p.add_argument('--cli', default=os.environ.get('SCENE_SETTINGS_CLI'))
    a = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.unity_version
    work = a.work.resolve()
    artifact_path(work)
    name = f'{a.suite}-{a.label}-p{a.profile}'
    project, out = work/name, work/(name+'.jsonl')
    if project.exists() or out.exists():
        p.error('refusing overwrite '+str(project))
    source, variable = SOURCES[a.suite]
    for folder in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(ROOT/folder, project/folder)
    shutil.rmtree(project/'Assets/Scripts')
    (project/'Assets/Scripts').mkdir()
    shutil.copy2(ROOT/'Sources'/source, project/'Assets/Scripts'/source)
    shutil.copy2(ROOT/'Sources/ContactEditor.cs', project/'Assets/Editor/ContactEditor.cs')
    settings = project/'ProjectSettings/DynamicsManager.asset'
    settings.write_text(re.sub(r'm_ContactPairsMode: \d+', f'm_ContactPairsMode: {a.profile}', settings.read_text()))
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.unity_version+'\n')
    if a.mode == 'native':
        cmd = [str(editor_path()), '-batchmode', '-nographics', '-projectPath', str(project),
               '-executeMethod', 'ContactEditor.Record', '-logFile', str(work/(name+'.editor.log'))]
    else:
        if not a.cli: p.error('--cli is required for engine captures')
        cmd = [a.cli, 'run', '-p', str(project), '--headless', '3000', '--no-watch']
    with (work/(name+'.log')).open('w') as log:
        result = subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '300', *cmd],
                                env=dict(os.environ, **{variable:str(out)}), stdout=log, stderr=subprocess.STDOUT)
    print(name, result.returncode, flush=True)
    if result.returncode or not out.exists(): raise SystemExit('capture failed: '+str(out))


if __name__ == '__main__': main()
