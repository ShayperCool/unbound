#!/usr/bin/env python3
"""Integration checks for the installed machine-wide job runner."""
import json
import importlib.util
import os
from pathlib import Path
import signal
import shlex
import subprocess
import sys
import tempfile
import time

RUNNER = Path(__file__).with_name('job_runner.py')

if len(sys.argv)>1 and sys.argv[1]=='worker':
    output, name, seconds = Path(sys.argv[2]),sys.argv[3],float(sys.argv[4])
    kind = sys.argv[5]
    assert os.environ['UseSharedCompilation']=='false'
    assert os.environ['MSBUILDDISABLENODEREUSE']=='1'
    assert os.environ['DOTNET_CLI_USE_MSBUILD_SERVER']=='0'
    if kind!='heavy':
        relative=Path('/proc/self/cgroup').read_text().split('::',1)[1].strip()
        group=Path('/sys/fs/cgroup')/relative.lstrip('/')
        pool=group.parent
        if kind in ('build','test','tool'):
            assert pool.name=='unbound-builds.slice',pool
            assert int((group/'memory.max').read_text())==9*2**30
            assert int((group/'memory.high').read_text())==7*2**30
        else:
            assert pool.name==('unbound-editors.slice' if kind.startswith('editor-') else 'unbound-engines.slice')
            assert int((pool/'memory.max').read_text())==(16 if kind.startswith('editor-') else 8)*2**30
        if kind.startswith('engine-'):
            assert int((group/'memory.max').read_text())==4*2**30
        assert int((pool.parent/'memory.max').read_text())==16*2**30
    fd = os.open(output,os.O_WRONLY|os.O_CREAT|os.O_APPEND,0o600)
    for phase in ['start','end']:
        os.write(fd,(json.dumps(dict(name=name,kind=kind,phase=phase,time=time.monotonic(),pid=os.getpid(),
            project=os.environ.get('TEST_ENGINE_PROJECT')))+'\n').encode())
        if phase=='start':
            time.sleep(seconds)
    os.close(fd)
    raise SystemExit(0)

out = Path(sys.argv[1])
out.mkdir(parents=True)
events = out/'events.jsonl'
# The checks run this checkout's runner with private lock files and policy, so they
# neither wait for nor block the jobs of the machine-wide pools.
state = out/'state'
state.mkdir()
(state/'job-limits.json').write_bytes(Path(__file__).with_name('job-limits.json').read_bytes())
os.environ['UNBOUND_JOB_RUNNER_STATE'] = str(state)

spec=importlib.util.spec_from_file_location('job_runner',Path(__file__).with_name('job_runner.py'))
runner=importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)
for command,expected in [
    (['timeout','600','/tmp/unbound','run','--headless','30000'],'engine-cpu'),
    (['/usr/bin/time','-v','-o','time.log','timeout','-s','KILL','600',
      '/tmp/unbound','run','--frames','8'],'engine-gpu'),
    (['env','KEY=value','timeout','600','/tmp/unbound','run','--headless','1'],'engine-cpu'),
    (['/tmp/unbound','run','--headless=1'],'engine-cpu'),
    (['python3','driver.py','/tmp/unbound','run','--headless','1'],'heavy'),
    (['cargo','build','--release'],'build'),
    (['cargo','run','--release','--','run','--frames','8'],'heavy'),
    (['env','A=1','timeout','600','cargo','run','--','run'],'heavy'),
    (['cargo','custom-alias'],'heavy'),
    (['timeout','1200','cargo','test','-p','unbound-app'],'build'),
    (['env','A=1','dotnet','test','Tests.csproj'],'test'),
    (['timeout','590','dotnet','test','Tests.csproj','-c','Release'],'test'),
    (['dotnet','build','project.csproj'],'build'),
    (['dotnet','exec','tool.dll'],'heavy'),
    (['./build.sh'],'heavy'),
    (['bash','-c','cargo build'],'heavy'),
    (['gdb','--args','/opt/Editor/Unity','-batchmode','-nographics'],'editor-cpu'),
    (['timeout','600','/tmp/unbound','compile','-p','x'],'tool'),
    (['dotnet','/tmp/api/ApiCheck.dll','a','b'],'tool'),
    (['python3','build-all.py'],'heavy'),
]:
    assert runner.classify(command)==expected,(command,expected)

project=out/'project';project.mkdir()
alias=out/'project-alias';alias.symlink_to(project.resolve(),target_is_directory=True)
assert runner.engine_project(['/tmp/unbound','run','-p',str(project)])==runner.engine_project(
    ['timeout','5','/tmp/unbound','run','--project='+str(alias)])
fake=out/'unbound'
fake.write_text('#!/usr/bin/env bash\nset -euo pipefail\nexport TEST_ENGINE_PROJECT="$3"\nshift 3\nexec '+
               shlex.quote(sys.executable)+' '+shlex.quote(str(Path(__file__).resolve()))+' "$@"\n')
fake.chmod(0o755)

def spawn(kind, name, seconds=.4, project=None):
    handle=(out/(name+'.log')).open('w')
    command=([str(fake),'run','-p',str(project)] if project else [sys.executable,__file__])
    process=subprocess.Popen([sys.executable,str(RUNNER),'--kind',kind,*command,
                              'worker',str(events),name,str(seconds),kind],stdout=handle,stderr=subprocess.STDOUT)
    return process,handle


def join(jobs):
    for process,handle in jobs:
        assert process.wait(timeout=25)==0,handle.name
        handle.close()


config=runner.policy()
EDITORS,ENGINES,GPUS=config['editor_slots'],config['engine_slots'],config['gpu_slots']
join([spawn('editor-cpu','cpu-'+str(i)) for i in range(EDITORS+2)])
join([spawn('engine-cpu','engine-'+str(i),.7) for i in range(ENGINES+1)])
join([spawn('engine-cpu','project-a-1',.7,project),
      spawn('engine-cpu','project-a-2',.7,project),
      spawn('engine-cpu','project-b',.7,out/'other-project')])
jobs=[spawn('engine-cpu','mix-engine-'+str(i),.9) for i in range(2)]
jobs += [spawn('editor-cpu','mix-editor-'+str(i),.9) for i in range(EDITORS)]
join(jobs)
jobs=[spawn('editor-cpu','reader-'+str(i),.8) for i in range(3)]
jobs.append(spawn('engine-cpu','reader-engine',.8))
time.sleep(.2)
jobs.append(spawn('heavy','writer',.3))
jobs.append(spawn('editor-cpu','late-reader',.2))
jobs.append(spawn('engine-cpu','late-engine',.2))
join(jobs)
# Builds overlap Editors and engines, each other up to build_slots, never the exclusive writer.
jobs=[spawn('build','build-'+str(i),.6) for i in range(2)]
jobs += [spawn('editor-cpu','build-editor-'+str(i),.6) for i in range(5)]
jobs.append(spawn('engine-cpu','build-engine',.6))
join(jobs)
join([spawn('editor-gpu','gpu-editor-'+str(i),.6) for i in range(GPUS)]+
     [spawn('engine-gpu','gpu-engine-'+str(i),.6) for i in range(2)])
# Tools run beside each other and the pools, not exclusively.
jobs=[spawn('tool','tool-'+str(i),.8) for i in range(config['tool_slots'])]
jobs.append(spawn('editor-cpu','tool-editor',.8))
join(jobs)
# Test runs do not take build slots: with every build slot busy, tests start at once.
jobs=[spawn('build','test-build-'+str(i),1.2) for i in range(config['build_slots'])]
time.sleep(.3)
jobs += [spawn('test','test-'+str(i),.6) for i in range(config['test_slots'])]
join(jobs)
marks={(row['name'],row['phase']):row['time'] for row in map(json.loads,events.read_text().splitlines())}
first_build_end=min(marks[('test-build-'+str(i),'end')] for i in range(config['build_slots']))
assert all(marks[('test-'+str(i),'start')]<first_build_end for i in range(config['test_slots'])),'tests waited for builds'
# A long-waiting build (behind another build) never delays an Editor.
jobs=[spawn('build','wait-build-a',3.0)]
time.sleep(.4)
jobs.append(spawn('build','wait-build-b',.2))
time.sleep(1.5)
jobs.append(spawn('editor-gpu','wait-editor',.2))
join(jobs)
# No head-of-line blocking: with every engine slot busy and more engines queued,
# an Editor whose own slot is free starts at once.
jobs=[spawn('engine-cpu','hol-engine-'+str(i),2.5) for i in range(ENGINES+2)]
time.sleep(.6)
jobs.append(spawn('editor-cpu','hol-editor',.2))
join(jobs)
rows=[json.loads(line) for line in events.read_text().splitlines()]
starts={r['name']:r['time'] for r in rows if r['phase']=='start'}
ends={r['name']:r['time'] for r in rows if r['phase']=='end'}
first_engine_end=min(ends['hol-engine-'+str(i)] for i in range(ENGINES+2))
assert starts['hol-editor']<first_engine_end,'an Editor waited behind queued engine jobs'
assert starts['wait-editor']<ends['wait-build-a'],'an Editor waited behind a queued build'
active=set()
peak=0
engine_peak=0
gpu_peak=0
mixed_overlap=False
build_overlap=False
active_kinds={}
active_projects={}
for row in sorted(rows,key=lambda r:r['time']):
    name=row['name']
    if row['phase']=='start':
        if name=='writer':
            assert not active,('writer overlaps',active)
        else:
            assert 'writer' not in active,('pooled job overlaps writer',name)
        active.add(name)
        if row.get('project'):
            assert row['project'] not in active_projects.values(),('project overlap',row,active_projects)
            active_projects[name]=row['project']
        active_kinds[name]=row['kind']
        engine_count=sum(k.startswith('engine-') for k in active_kinds.values())
        editor_count=sum(k.startswith('editor-') for k in active_kinds.values())
        build_count=sum(k=='build' for k in active_kinds.values())
        test_count=sum(k=='test' for k in active_kinds.values())
        assert test_count<=config['test_slots'],('test cap',active_kinds)
        tool_count=sum(k=='tool' for k in active_kinds.values())
        assert tool_count<=config['tool_slots'],('tool cap',active_kinds)
        tool_peak=max(globals().get('tool_peak',0),tool_count)
        globals()['tool_peak']=tool_peak
        gpu_count=sum(k.endswith('-gpu') for k in active_kinds.values())
        assert engine_count<=ENGINES,('engine cap',active_kinds)
        assert editor_count<=EDITORS,('Editor cap',active_kinds)
        assert build_count<=config['build_slots'],('build cap',active_kinds)
        assert gpu_count<=GPUS,('GPU cap',active_kinds)
        build_overlap |= build_count>0 and editor_count+engine_count>0
        engine_peak=max(engine_peak,engine_count)
        gpu_peak=max(gpu_peak,gpu_count)
        mixed_overlap |= engine_count>0 and editor_count>0
        if name.startswith('cpu-'):
            peak=max(peak,sum(n.startswith('cpu-') for n in active))
    else:
        assert name in active,name
        active.remove(name)
        active_projects.pop(name,None)
        del active_kinds[name]
assert not active
assert peak==EDITORS,('Editor jobs did not fill their slots',peak)
assert engine_peak==ENGINES,('engine jobs did not fill their slots',engine_peak)
assert gpu_peak==GPUS,('GPU jobs did not fill their slots',gpu_peak)
assert globals().get('tool_peak',0)==config['tool_slots'],'tools were serialized'
assert mixed_overlap,'Editor and engine never overlapped'
assert build_overlap,'builds still stop the Editor and engine pools'

for kind in ['editor-cpu','engine-cpu']:
    name='cancel-'+kind
    process,handle=spawn(kind,name,20)
    deadline=time.monotonic()+10
    pid=None
    while time.monotonic()<deadline:
        for line in events.read_text().splitlines():
            row=json.loads(line)
            if row['name']==name and row['phase']=='start':
                pid=row['pid']
        if pid:
            break
        time.sleep(.05)
    assert pid,'cancellation worker never started'
    process.terminate()
    code=process.wait(timeout=10)
    handle.close()
    assert code!=0
    deadline=time.monotonic()+5
    while Path('/proc',str(pid)).exists() and time.monotonic()<deadline:
        time.sleep(.05)
    assert not Path('/proc',str(pid)).exists(),'cancelled worker is still alive'
    join([spawn('heavy','after-'+name,.05)])
(out/'summary.json').write_text(json.dumps(dict(editor_peak=peak,slot_limit=EDITORS,
    engine_peak=engine_peak,engine_slot_limit=ENGINES,gpu_peak=gpu_peak,gpu_slot_limit=GPUS,
    mixed_overlap=mixed_overlap,no_head_of_line_blocking=True,
    combined_memory_max_bytes=16*2**30,engine_memory_max_bytes=8*2**30,
    engine_job_memory_max_bytes=4*2**30,swap_allowed=True,exclusive_builds=False,build_slots=config['build_slots'],
    build_overlap=build_overlap,project_serial=True,cancellation=True),indent=2)+'\n')
print('Editor/engine/build/GPU slots, no head-of-line blocking, exclusive writer and cancellation verified;',
      'Editor peak',peak,'engine peak',engine_peak,'GPU peak',gpu_peak)
