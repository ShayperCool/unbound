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

RUNNER = Path.home()/'.cache/compat/with-heavy-job'

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
        assert pool.name==('unbound-editors.slice' if kind.startswith('editor-') else 'unbound-engines.slice')
        assert int((pool/'memory.max').read_text())==(16 if kind.startswith('editor-') else 8)*2**30
        if kind.startswith('engine-'):
            assert int((group/'memory.max').read_text())==4*2**30
            assert int((group/'memory.swap.max').read_text())==0
        assert int((pool/'memory.swap.max').read_text())==0
        assert int((pool.parent/'memory.max').read_text())==16*2**30
        assert int((pool.parent/'memory.swap.max').read_text())==0
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
    (['cargo','build','--release'],'heavy'),
    (['dotnet','build','project.csproj'],'heavy'),
    (['gdb','--args','/opt/Editor/Unity','-batchmode','-nographics'],'editor-cpu'),
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
    process=subprocess.Popen([str(RUNNER),'--kind',kind,*command,
                              'worker',str(events),name,str(seconds),kind],stdout=handle,stderr=subprocess.STDOUT)
    return process,handle


def join(jobs):
    for process,handle in jobs:
        assert process.wait(timeout=25)==0,handle.name
        handle.close()


join([spawn('editor-cpu','cpu-'+str(i)) for i in range(10)])
join([spawn('engine-cpu','engine-'+str(i),.7) for i in range(5)])
join([spawn('engine-cpu','project-a-1',.7,project),
      spawn('engine-cpu','project-a-2',.7,project),
      spawn('engine-cpu','project-b',.7,out/'other-project')])
jobs=[spawn('engine-cpu','mix-engine-'+str(i),.9) for i in range(2)]
jobs += [spawn('editor-cpu','mix-editor-'+str(i),.9) for i in range(8)]
join(jobs)
jobs=[spawn('editor-cpu','reader-'+str(i),.8) for i in range(3)]
jobs.append(spawn('engine-cpu','reader-engine',.8))
time.sleep(.2)
jobs.append(spawn('heavy','writer',.3))
jobs.append(spawn('editor-cpu','late-reader',.2))
jobs.append(spawn('engine-cpu','late-engine',.2))
join(jobs)
join([spawn('editor-gpu','gpu-editor-'+str(i),.3) for i in range(2)]+
     [spawn('engine-gpu','gpu-engine-'+str(i),.3) for i in range(2)])
rows=[json.loads(line) for line in events.read_text().splitlines()]
active=set()
peak=0
engine_peak=0
mixed_overlap=False
active_kinds={}
active_projects={}
for row in sorted(rows,key=lambda r:r['time']):
    name=row['name']
    if row['phase']=='start':
        if name=='writer':
            assert not active,('writer overlaps',active)
        else:
            assert 'writer' not in active,('pooled job overlaps writer',name)
        if name.startswith('gpu-'):
            assert not any(n.startswith('gpu-') for n in active)
        active.add(name)
        if row.get('project'):
            assert row['project'] not in active_projects.values(),('project overlap',row,active_projects)
            active_projects[name]=row['project']
        active_kinds[name]=row['kind']
        engine_count=sum(k.startswith('engine-') for k in active_kinds.values())
        editor_count=sum(k.startswith('editor-') for k in active_kinds.values())
        assert engine_count<=2,('engine cap',active_kinds)
        assert editor_count+engine_count*2<=8,('shared memory tokens',active_kinds)
        engine_peak=max(engine_peak,engine_count)
        mixed_overlap |= engine_count>0 and editor_count>0
        if name.startswith('cpu-'):
            peak=max(peak,sum(n.startswith('cpu-') for n in active))
            assert peak<=8,peak
    else:
        assert name in active,name
        active.remove(name)
        active_projects.pop(name,None)
        del active_kinds[name]
assert not active
assert peak>=2,'Editor jobs were still serialized'
assert engine_peak==2,'engine jobs were still serialized'
assert mixed_overlap,'Editor and engine never overlapped'

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
(out/'summary.json').write_text(json.dumps(dict(editor_peak=peak,slot_limit=8,
    engine_peak=engine_peak,engine_slot_limit=2,mixed_overlap=mixed_overlap,
    combined_memory_tokens=8,combined_memory_max_bytes=16*2**30,
    engine_memory_max_bytes=8*2**30,engine_job_memory_max_bytes=4*2**30,swap_max_bytes=0,
    exclusive_builds=True,gpu_serial=True,project_serial=True,cancellation=True),indent=2)+'\n')
print('Editor/engine slots, combined memory budget, exclusive writer, GPU mutex and cancellation verified;',
      'Editor peak',peak,'engine peak',engine_peak)
