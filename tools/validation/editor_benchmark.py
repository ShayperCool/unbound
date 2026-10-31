#!/usr/bin/env python3
"""Measure fresh independent Unity processes within a bounded systemd slice."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tests'))
from probe_paths import editor_path

REPO = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--work', type=Path, required=True)
parser.add_argument('--label', required=True)
parser.add_argument('--count', type=int, required=True)
parser.add_argument('--profile', choices=['motion', 'queries', 'clipping'], default='motion')
parser.add_argument('--reuse', type=Path)
parser.add_argument('--launcher', choices=['direct','pool'], default='direct')
args = parser.parse_args()
out = args.work / args.label
out.mkdir(parents=True)
slice_name = 'unbound-editorbench.slice' if args.launcher=='direct' else 'unbound-editors.slice'
editor = editor_path()
source = REPO/'tests/projects'/('uirectclipping' if args.profile=='clipping' else 'physics')
jobs = []
for index in range(args.count):
    job = out/str(index)
    job.mkdir()
    project = args.reuse/str(index)/'project' if args.reuse else job/'project'
    if not args.reuse:
        for folder in ['Assets','Packages','ProjectSettings']:
            shutil.copytree(source/folder,project/folder)
        if args.profile!='clipping':
            probe = 'physics2dmotion' if args.profile=='motion' else 'query2dties'
            shutil.copy2(REPO/'tests/projects'/probe/'Sources/ProbeEntry.cs',
                         project/'Assets/Scripts/ProbeEntry.cs')
    scope_id = uuid.uuid4().hex
    unit = ('unbound-editorbench-' if args.launcher=='direct' else 'unbound-editor-')+scope_id+'.scope'
    prefix = (['systemd-run','--user','--scope','--quiet','--unit='+unit,'--slice='+slice_name]
              if args.launcher=='direct' else
              [str(Path.home()/'.cache/compat/with-heavy-job'),'--scope-id',scope_id])
    command = prefix+['/usr/bin/time','-v','-o',str(job/'time.log'),'timeout','600',str(editor),'-batchmode']
    if args.profile!='clipping':
        command.append('-nographics')
    command += ['-projectPath',str(project),'-executeMethod','ProbeRunner.Record',
                '-logFile',str(job/'editor.log')]
    env = dict(os.environ)
    env['PHYSICS_PROBE_OUT'] = str(job/'capture.json')
    env['RECT_CLIP_OUT'] = str(job/'capture')
    handle = (job/'stdout.log').open('w')
    process = subprocess.Popen(command,env=env,stdout=handle,stderr=subprocess.STDOUT)
    jobs.append(dict(process=process,handle=handle,unit=unit,job=job,command=command,
                     peak_bytes=0,swap_peak_bytes=0))

def memory(path):
    try:
        return int(path.read_text())
    except (OSError,ValueError):
        return 0

cg = Path('/sys/fs/cgroup')/'user.slice'/('user-'+str(os.getuid())+'.slice')/('user@'+str(os.getuid())+'.service')/'unbound.slice'/slice_name
samples = []
start = time.monotonic()
try:
    while True:
        live = [j for j in jobs if j['process'].poll() is None]
        now = time.monotonic()-start
        current = memory(cg/'memory.current')
        sample = dict(seconds=round(now,3),memory_bytes=current,
                      swap_bytes=memory(cg/'memory.swap.current'),running=len(live))
        samples.append(sample)
        for j in live:
            group = cg/j['unit']
            j['peak_bytes'] = max(j['peak_bytes'],memory(group/'memory.peak'),memory(group/'memory.current'))
            j['swap_peak_bytes'] = max(j['swap_peak_bytes'],memory(group/'memory.swap.current'))
        if not live:
            break
        if now>660:
            raise TimeoutError('Editor benchmark exceeded 660 seconds')
        time.sleep(.1)
finally:
    for j in jobs:
        if j['process'].poll() is None:
            subprocess.run(['systemctl','--user','kill','--signal=TERM',j['unit']],check=False,
                           stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            j['process'].wait(timeout=15)
        j['handle'].close()

result = dict(label=args.label,profile=args.profile,count=args.count,launcher=args.launcher,
              cold=args.reuse is None,seconds=round(time.monotonic()-start,3),
              pool_peak_bytes=max(s['memory_bytes'] for s in samples),
              pool_swap_peak_bytes=max(s['swap_bytes'] for s in samples),jobs=[])
for j in jobs:
    path = j['job']/'capture.json'
    files = [path] if path.is_file() else sorted((j['job']/'capture').glob('*'))
    captures = {str(p.relative_to(j['job'])):hashlib.sha256(p.read_bytes()).hexdigest()
                for p in files if p.is_file()}
    result['jobs'].append(dict(unit=j['unit'],exit_code=j['process'].returncode,
                              peak_bytes=j['peak_bytes'],swap_peak_bytes=j['swap_peak_bytes'],
                              captures=captures,command=j['command']))
(out/'samples.json').write_text(json.dumps(samples,indent=2)+'\n')
(out/'summary.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2),flush=True)
assert all(j['exit_code']==0 and j['captures'] for j in result['jobs']), 'failed Editor run'
