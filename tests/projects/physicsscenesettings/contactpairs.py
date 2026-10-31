#!/usr/bin/env python3
"""Supplement the original recordings with direct contact queries, preserving every old key."""
from pathlib import Path
import os,pathlib,shutil,subprocess,sys
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, run_root

from run import ROOT
work=pathlib.Path(os.environ.get('SCENE_WORK', run_root(ROOT, 'matrix'))); phase=sys.argv[1]
heavy=pathlib.Path.home()/'.cache/compat/with-heavy-job'
noise = '--noise' in sys.argv
label = 'islands' if noise else 'contacts'
profiles = ['default','determinism','tgs','tgs-det'] if noise else ['default','pairs-kk','pairs-ks','pairs-all']
for name in profiles:
    project=work/(label+'-projects')/name
    if not project.exists():
        for d in ['Assets','ProjectSettings','Packages']:
            shutil.copytree(work/'projects'/name/d,project/d,dirs_exist_ok=True)
    shutil.copy2(ROOT/'Sources/supplemental/Scripts/SceneSettingsProbe.cs',project/'Assets/Scripts/SceneSettingsProbe.cs')
    shutil.copy2(ROOT/'Sources/supplemental/Editor/ProbeRunner.cs',project/'Assets/Editor/ProbeRunner.cs')
    for repeat in ([1,2] if phase=='native' else [1]):
        folder=work/(f'{label}-native{repeat}' if phase=='native' else label+'-'+phase);folder.mkdir(exist_ok=True)
        output=folder/(name+'.jsonl'); env=dict(os.environ,SCENE_SETTINGS_QUERY_CONTACTS='0' if noise else '1',SCENE_SETTINGS_NOISE='1' if noise else '0',SCENE_SETTINGS_OUT=str(output))
        if phase=='native':
            cmd=[str(editor_path()),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','ProbeRunner.Record','-logFile',str(folder/(name+'.log'))]
        else:
            cli=pathlib.Path.home()/'.cache/compat/parity-baseline-native6/unbound' if phase=='baseline' else pathlib.Path(os.environ.get('SCENE_SETTINGS_CLI',str(work/'cargo/release/unbound')))
            cmd=[str(cli),'run','-p',str(project),'--headless','30000','--no-watch']
        with (folder/(name+'.stdout')).open('w') as f:
            code=subprocess.run([str(heavy),'timeout','240',*cmd],env=env,stdout=f,stderr=subprocess.STDOUT).returncode
        print(phase,name,repeat,'exit',code,flush=True)
        if code or not output.exists(): raise SystemExit(str(output)+' failed')
