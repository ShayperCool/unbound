#!/usr/bin/env python3
"""Compare serial and pooled motion captures with the same warmed projects."""
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

REPO=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(REPO/'tests'))
from probe_paths import cache_artifact_path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--work',type=Path,required=True)
parser.add_argument('--release',required=True)
parser.add_argument('--cli',type=Path,required=True)
parser.add_argument('--managed',type=Path,required=True)
args=parser.parse_args()
args.work=cache_artifact_path(args.work).resolve()
args.cli=args.cli.resolve()
args.managed=args.managed.resolve()
source=REPO/'tests/projects/physics'
version=(source/'ProjectSettings/ProjectVersion.txt').read_text()
assert 'm_EditorVersion: '+args.release+'\n' in version,version
reference=REPO/'tests/projects/physics2dmotion/Reference'/('unity-'+args.release)/'motion.json.gz'
assert reference.is_file(),reference
args.work.mkdir(parents=True)
runner=Path.home()/'.cache/compat/with-heavy-job'
group=Path('/sys/fs/cgroup/user.slice')/('user-'+str(os.getuid())+'.slice')/(
    'user@'+str(os.getuid())+'.service')/'unbound.slice'
result=dict(release=args.release,cli=str(args.cli),managed=str(args.managed),
            artifacts_sha256={},phases=[],inputs=[],reference=str(reference),
            reference_sha256=hashlib.sha256(reference.read_bytes()).hexdigest())
for path in [args.cli,args.managed/'UnityEngine.CoreModule.dll',args.managed/'libunbound_physx.so']:
    result['artifacts_sha256'][str(path)]=hashlib.sha256(path.read_bytes()).hexdigest()
for index in range(2):
    project=args.work/str(index)/'project'
    for folder in ['Assets','Packages','ProjectSettings']:
        shutil.copytree(source/folder,project/folder)
    shutil.copy2(REPO/'tests/projects/physics2dmotion/Sources/ProbeEntry.cs',
                 project/'Assets/Scripts/ProbeEntry.cs')
    manifest={str(path.relative_to(project)):hashlib.sha256(path.read_bytes()).hexdigest()
              for folder in ['Assets','Packages','ProjectSettings']
              for path in sorted((project/folder).rglob('*')) if path.is_file()}
    result['inputs'].append(manifest)
assert result['inputs'][0]==result['inputs'][1]

def memory(name):
    try:return int((group/name).read_text())
    except FileNotFoundError:return 0

def start(phase,index):
    folder=args.work/str(index)
    output=folder/(phase+'.json')
    env=dict(os.environ,PHYSICS_PROBE_OUT=str(output),UNBOUND_MANAGED_DIR=str(args.managed),
             LD_LIBRARY_PATH=str(args.managed),UseSharedCompilation='false',
             MSBUILDDISABLENODEREUSE='1',DOTNET_CLI_USE_MSBUILD_SERVER='0')
    command=[str(runner),'--scope-id',uuid.uuid4().hex,'timeout','600',str(args.cli),'run',
             '-p',str(folder/'project'),'--headless','30000','--no-watch']
    log=(folder/(phase+'.log')).open('x')
    return dict(process=subprocess.Popen(command,env=env,stdout=log,stderr=subprocess.STDOUT),
                log=log,output=output,command=command,index=index)

def wait(jobs,record):
    deadline=time.monotonic()+650
    try:
        while any(job['process'].poll() is None for job in jobs):
            record['memory_peak_bytes']=max(record['memory_peak_bytes'],memory('memory.current'))
            record['swap_peak_bytes']=max(record['swap_peak_bytes'],memory('memory.swap.current'))
            if time.monotonic()>deadline:raise TimeoutError('engine benchmark timed out')
            time.sleep(.05)
        for job in jobs:
            code=job['process'].wait()
            entry=dict(index=job['index'],command=job['command'],exit_code=code,
                       output=str(job['output']))
            if job['output'].exists():
                entry['sha256']=hashlib.sha256(job['output'].read_bytes()).hexdigest()
                entry['bytes']=job['output'].stat().st_size
            record['jobs'].append(entry)
            assert code==0 and 'sha256' in entry,entry
    finally:
        for job in jobs:
            if job['process'].poll() is None:
                job['process'].terminate()
                job['process'].wait(timeout=15)
            job['log'].close()

try:
    # Warm each project's managed compilation and imported assets separately.
    # The timed phases restart the same binary in the same two projects.
    for phase,parallel in [('warm',False),('serial',False),('parallel',True)]:
        record=dict(phase=phase,parallel=parallel,jobs=[],memory_peak_bytes=0,swap_peak_bytes=0)
        result['phases'].append(record)
        began=time.monotonic()
        if parallel:
            wait([start(phase,index) for index in range(2)],record)
        else:
            for index in range(2):wait([start(phase,index)],record)
        record['seconds']=round(time.monotonic()-began,3)
        print(json.dumps(record),flush=True)
    hashes={job['sha256'] for phase in result['phases'] for job in phase['jobs']}
    result['captures_identical']=len(hashes)==1
    result['speedup']=round(result['phases'][1]['seconds']/result['phases'][2]['seconds'],3)
    # Use the existing release oracle and its unchanged tolerance. The report
    # retains every missing/extra/failing value, including pre-existing gaps.
    result['canonical_report']=str(args.work/'parity.json')
    with (args.work/'parity.log').open('x') as log:
        subprocess.run([sys.executable,str(REPO/'tests/projects/physics2dmotion/report.py'),
            'parallel','--work',str(args.work/'0'),'--baseline','serial',
            '--output',result['canonical_report']],
            env=dict(os.environ,UNBOUND_PROBE_UNITY_VERSION=args.release),
            check=True,stdout=log,stderr=subprocess.STDOUT)
    assert result['captures_identical'],'captures changed; full outputs retained for comparison'
    assert all(phase['swap_peak_bytes']==0 for phase in result['phases'])
finally:
    (args.work/'summary.json').write_text(json.dumps(result,indent=2)+'\n')
