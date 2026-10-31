#!/usr/bin/env python3
"""Change only DynamicsManager in one private project with an already populated Library."""
import os,pathlib,shutil,subprocess
from run import ROOT
work=pathlib.Path(os.environ['SCENE_WORK']); project=work/'cache-project'
for d in ['Assets','ProjectSettings','Packages']:
    shutil.copytree(work/'projects/default'/d,project/d,dirs_exist_ok=True)
out=work/'cache-final';out.mkdir(exist_ok=True)
settings=project/'ProjectSettings/DynamicsManager.asset';original=settings.read_text()
try:
    for name in ['default','one','restored']:
        settings.write_text(original.replace('m_FrictionType: 0','m_FrictionType: 1') if name=='one' else original)
        env=dict(os.environ,SCENE_SETTINGS_OUT=str(out/(name+'.jsonl')))
        with (out/(name+'.log')).open('w') as f:
            subprocess.run([str(pathlib.Path.home()/'.cache/compat/with-heavy-job'),'timeout','240',str(pathlib.Path(os.environ.get('SCENE_SETTINGS_CLI',str(work/'cargo/release/unbound')))),'run','-p',str(project),'--headless','30000','--no-watch'],env=env,stdout=f,stderr=subprocess.STDOUT,check=True)
        ref=ROOT/'Reference'/('one.jsonl' if name=='one' else 'default.jsonl')
        with (out/(name+'-comparison.txt')).open('w') as f:
            code=subprocess.run(['python3',str(ROOT/'compare.py'),str(ref),str(out/(name+'.jsonl'))],stdout=f).returncode
        print(name,'comparison',code,flush=True)
finally: settings.write_text(original)
