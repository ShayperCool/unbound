#!/usr/bin/env python3
"""Private Unity-compatible creation and append-only Native/engine captures."""
import sys
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, run_root

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
HEAVY = Path.home() / '.cache/compat/with-heavy-job'
UNITY = editor_path()


def main():
    p = argparse.ArgumentParser()
    p.add_argument('mode', choices=['create', 'checkout', 'prepare', 'native', 'engine'])
    p.add_argument('--order', choices=['transform-first', 'rigidbody-first'], default='transform-first')
    p.add_argument('--label', default='native1')
    a = p.parse_args()
    w = Path(os.environ.get('LOADING_WORK', run_root(ROOT, 'loading'))).resolve()
    project = w / ('created' if a.mode in ('create', 'checkout') else a.order + '-' + a.label)
    log = w / 'logs' / (a.mode + '-' + a.order + '-' + a.label + '.log')
    log.parent.mkdir(parents=True, exist_ok=True)
    if a.mode == 'checkout':
        # The checked-in Assets are what `create` authors (Sources plus the scene and prefabs
        # LoadingEditor.Create made); Packages and ProjectSettings are the base project's.
        if project.exists(): raise SystemExit(f'refusing to overwrite {project}')
        base = REPO/'tests/projects/physicsscenesettings'
        tree = lambda root, folder: {f.relative_to(root): f.read_bytes() for f in (root/folder).rglob('*') if f.is_file()}
        for folder in ['Packages','ProjectSettings']:
            if tree(ROOT, folder) != tree(base, folder):
                raise SystemExit(f'{folder} differs from the base project: run create in the Editor')
        for name in ['LoadingProbe.cs', 'LoadingEditor.cs']:
            copy = ROOT/'Assets'/('Editor' if 'Editor' in name else 'Scripts')/name
            if copy.read_bytes() != (ROOT/'Sources'/name).read_bytes():
                raise SystemExit(f'{copy} differs from Sources/{name}: run create in the Editor')
        for folder in ['Assets','Packages','ProjectSettings']:
            shutil.copytree(ROOT/folder,project/folder)
        return
    if a.mode == 'prepare':
        if project.exists(): raise SystemExit(f'refusing to overwrite {project}')
        for folder in ['Assets','Packages','ProjectSettings']:
            shutil.copytree(w/'created'/folder,project/folder)
        # Reordering complete YAML documents preserves Unity references and component list order.
        for path in [*project.glob('Assets/**/*.unity'),*project.glob('Assets/**/*.prefab')]:
            text = path.read_text(); parts = re.split(r'(?=^--- !u!)',text,flags=re.M)
            header, docs = parts[0],parts[1:]
            def rank(doc):
                cid = int(re.match(r'--- !u!(\d+)',doc)[1])
                return (0 if cid == (4 if a.order == 'transform-first' else 54) else 1)
            path.write_text(header + ''.join(sorted(docs,key=rank)))
        return
    if a.mode == 'create':
        for folder in ['Assets','Packages','ProjectSettings']:
            shutil.copytree(REPO/'tests/projects/physicsscenesettings'/folder,project/folder)
        shutil.rmtree(project/'Assets/Scripts'); shutil.rmtree(project/'Assets/Editor')
        (project/'Assets/Scripts').mkdir(); (project/'Assets/Editor').mkdir()
        shutil.copyfile(ROOT/'Sources/LoadingProbe.cs',project/'Assets/Scripts/LoadingProbe.cs')
        shutil.copyfile(ROOT/'Sources/LoadingEditor.cs',project/'Assets/Editor/LoadingEditor.cs')
    out = w / (a.order + '-' + a.label + '.jsonl')
    if out.exists() or log.exists(): raise SystemExit('refusing to overwrite capture/log')
    env = dict(os.environ,LOADING_OUT=str(out))
    if a.mode in ['create','native']:
        cmd = [str(UNITY),'-batchmode','-nographics','-projectPath',str(project),
               '-executeMethod','LoadingEditor.' + ('Create' if a.mode == 'create' else 'Record'),
               '-logFile',str(log.with_suffix('.editor.log'))]
    else:
        cmd = [os.environ['SCENE_SETTINGS_CLI'],'run','-p',str(project),'--headless','30000','--no-watch']
    with log.open('w') as f:
        r = subprocess.run([str(HEAVY),'timeout','300',*cmd],env=env,stdout=f,stderr=subprocess.STDOUT)
    print(a.mode,a.order,a.label,'exit',r.returncode,flush=True)
    if r.returncode or (a.mode != 'create' and not out.exists()): raise SystemExit(f'failed: {log}')


if __name__ == '__main__': main()
