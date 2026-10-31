#!/usr/bin/env python3
"""Private Unity-compatible creation and append-only Native/engine captures."""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
HEAVY = Path.home() / '.cache/compat/with-heavy-job'
UNITY = Path.home() / 'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'


def main():
    p = argparse.ArgumentParser()
    p.add_argument('mode', choices=['create', 'prepare', 'native', 'engine'])
    p.add_argument('--order', choices=['transform-first', 'rigidbody-first'], default='transform-first')
    p.add_argument('--label', default='native1')
    a = p.parse_args()
    w = Path(os.environ['LOADING_WORK']).resolve()
    project = w / ('created' if a.mode == 'create' else a.order + '-' + a.label)
    log = w / 'logs' / (a.mode + '-' + a.order + '-' + a.label + '.log')
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
