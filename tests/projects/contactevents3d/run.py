#!/usr/bin/env python3
"""Append-only public ContactEvent recordings in independent private projects."""
import argparse,os,shutil,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[2]
def main():
    p=argparse.ArgumentParser();p.add_argument('mode',choices=['create','native','engine']);p.add_argument('--label',default='native1');p.add_argument('--profile',type=int,default=0);a=p.parse_args()
    work=Path(os.environ['CONTACT_WORK']);project=work/(a.label+'-p'+str(a.profile));out=work/(a.label+'-p'+str(a.profile)+'.jsonl');log=out.with_suffix('.log')
    if project.exists() or out.exists() or log.exists():raise SystemExit('refusing overwrite '+str(project))
    source=REPO/'tests/projects/physicsscenesettings' if a.mode=='create' else work/'created-p0'
    for d in ['Assets','Packages','ProjectSettings']:shutil.copytree(source/d,project/d)
    shutil.rmtree(project/'Assets/Scripts');shutil.rmtree(project/'Assets/Editor');(project/'Assets/Scripts').mkdir();(project/'Assets/Editor').mkdir()
    shutil.copy2(ROOT/'Sources/ContactProbe.cs',project/'Assets/Scripts/ContactProbe.cs');shutil.copy2(ROOT/'Sources/ContactEditor.cs',project/'Assets/Editor/ContactEditor.cs')
    settings=project/'ProjectSettings/DynamicsManager.asset';text=settings.read_text();import re
    text=re.sub(r'm_ContactPairsMode: \d+',f'm_ContactPairsMode: {a.profile}',text);settings.write_text(text)
    env=dict(os.environ,CONTACT_OUT=str(out))
    if a.mode=='engine':cmd=[os.environ['SCENE_SETTINGS_CLI'],'run','-p',str(project),'--headless','3000','--no-watch']
    else:cmd=[str(Path.home()/'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','ContactEditor.'+('Create' if a.mode=='create' else 'Record'),'-logFile',str(out.with_suffix('.editor.log'))]
    with log.open('w') as f:r=subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'),'timeout','300',*cmd],env=env,stdout=f,stderr=subprocess.STDOUT)
    print(a.mode,a.label,a.profile,r.returncode,flush=True)
    if r.returncode or (a.mode!='create' and not out.exists()):raise SystemExit('failed '+str(log))
if __name__=='__main__':main()
