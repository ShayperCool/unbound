#!/usr/bin/env python3
"""Create an independent private project and capture offline Unity audio."""
import argparse
import array
import json
import os
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
a = p.parse_args()
fixture = Path(__file__).resolve().parent
if (fixture / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work / 'project'
shutil.copytree(fixture, project, ignore=shutil.ignore_patterns('Reference', 'Library', 'Logs', '__pycache__'))
output = a.work / 'capture'
editor = a.editor or Path.home() / 'Unity/Hub/Editor' / a.release / 'Editor/Unity'
commands=[]
for stage in ('Prepare','Record'):
    command=[str(Path.home()/'.cache/compat/with-heavy-job'),'timeout','900',str(editor),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','ProbeRunner.'+stage,'-logFile',str(a.work/(stage.lower()+'.log'))]
    commands.append(command)
    (a.work/'commands.json').write_text(json.dumps(commands,indent=2)+'\n')
    with (a.work/(stage.lower()+'-stdout.log')).open('w') as log:
        subprocess.run(command,env=dict(os.environ,UseSharedCompilation='false',MSBUILDDISABLENODEREUSE='1',DOTNET_CLI_USE_MSBUILD_SERVER='0',SUSPEND_PROBE_OUT=str(output),SUSPEND_PROBE_RATE=str(a.rate)),stdout=log,stderr=subprocess.STDOUT,check=True)
from verify import verify
verify(output, a.release, a.rate)

from pack import pack
pack(output)
