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
p.add_argument('--reset', action='store_true')
p.add_argument('--reset-playback', action='store_true')
p.add_argument('--submission', action='store_true')
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
a = p.parse_args()
if sum((a.reset,a.reset_playback,a.submission))>1: p.error('choose one reset profile')
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
    command=[str(Path.home()/'.cache/compat/with-heavy-job'),'timeout','900',str(editor),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod',('SubmissionRunner.Record' if a.submission and stage=='Record' else 'ResetPlaybackRunner.Record' if a.reset_playback and stage=='Record' else 'ResetRunner.Record' if a.reset and stage=='Record' else 'ProbeRunner.'+stage),'-logFile',str(a.work/(stage.lower()+'.log'))]
    commands.append(command)
    (a.work/'commands.json').write_text(json.dumps(commands,indent=2)+'\n')
    with (a.work/(stage.lower()+'-stdout.log')).open('w') as log:
        subprocess.run(command,env=dict(os.environ,UseSharedCompilation='false',MSBUILDDISABLENODEREUSE='1',DOTNET_CLI_USE_MSBUILD_SERVER='0',SUSPEND_PROBE_OUT=str(output),SUSPEND_PROBE_RATE=str(a.rate)),stdout=log,stderr=subprocess.STDOUT,check=True)
if a.submission:
    from verify_submission import verify
elif a.reset_playback:
    from verify_reset_playback import verify
elif a.reset:
    from verify_reset import verify
else:
    from verify import verify
verify(output, a.release, a.rate)

from pack import pack
pack(output)
