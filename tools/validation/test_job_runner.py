#!/usr/bin/env python3
"""Integration checks for the installed machine-wide job runner."""
import json
import fcntl
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
    (['/usr/bin/time','-a','-ofile','timeout','-vsTERM','600','env','--','KEY=value',
      '/tmp/unbound','run','--headless','1'],'engine-cpu'),
    (['env','-uKEY','-C/tmp','--','KEY=value','cargo','build'],'build'),
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

# Refuse self-leasing drivers before consulting policy or taking any lock, even
# with explicit kinds, wrappers, Python options and an occupied entry turnstile.
driver = str(RUNNER.resolve().parents[1]/'build.py')
build_commands = [
    ['python3','tools/build.py'],
    [sys.executable,'-I',driver],
    [sys.executable,'-I','--check-hash-based-pycs','always',driver],
    [sys.executable,'-IX','dev',driver],
    ['tools/build.py'],
    ['/usr/bin/time','-o',str(out/'wrapped-time.log'),'timeout','-s','TERM','2',
     'env','CHECK=1',sys.executable,'-I','-W','ignore',driver],
    ['env','--chdir=/tmp','timeout','2',sys.executable,'-I',driver],
    ['/usr/bin/time','-a','-o',str(out/'append-time.log'),'env','--','CHECK=1',
     'timeout','-vsTERM','2',sys.executable,'-IWignore',driver],
]
for command in build_commands:
    assert runner.is_build_driver(command),command
for command in [
    ['python3','driver.py','tools/build.py'],
    ['python3','-c','pass','tools/build.py'],
    ['python3','-m','driver','tools/build.py'],
    ['python3','-Ic','pass','tools/build.py'],
    ['python3','-c','pass','--unknown-script-option','tools/build.py'],
    ['python3','-m','driver','--unknown-script-option','tools/build.py'],
    ['python3','other/build.py'],
]:
    assert not runner.is_build_driver(command),command
with runner.lock('parity-heavy-entry.lock'), runner.lock('parity-heavy-job.lock'):
    for command in build_commands:
        for options in [[],['--kind','build'],['--kind','heavy'],['--pool-worker','build']]:
            result = subprocess.run([sys.executable,str(RUNNER),*options,'--',*command,
                                     '--','build','--release','--locked','-p','unbound'],
                                    capture_output=True,text=True,timeout=3)
            assert result.returncode==125,(command,result)
            assert 'run tools/build.py directly' in result.stderr,result.stderr
    for split in [['-S','python3 -I'], ['-Spython3 -I'], ['-iS','python3 -I'],
                  ['--split-string','python3 -I'], ['--split-string=python3 -I'],
                  ['--split-s=python3 -I']]:
        result = subprocess.run([sys.executable,str(RUNNER),'/usr/bin/time','-a','timeout','2',
                                 'env',*split,driver,'--','build','--release','--locked','-p','unbound'],
                                capture_output=True,text=True,timeout=3)
        assert result.returncode==125,(split,result)
        assert 'env split-string wrappers are unsupported' in result.stderr,result.stderr
        assert 'Run tools/build.py directly' in result.stderr,result.stderr
    # Unknown options cannot silently hide the executable behind an operand.
    result = subprocess.run([sys.executable,str(RUNNER),'env','--unknown-option','value',
                             sys.executable,driver],capture_output=True,text=True,timeout=3)
    assert result.returncode==125,result
    assert 'cannot inspect env wrapper' in result.stderr,result.stderr
    for command in [
        ['env','CHECK=1','-S','python3 -I',driver],
        ['env','-','-S','python3 -I',driver],
        ['env','--','CHECK=1','--split-string=python3 -I',driver],
        ['env','CHECK=1','-u','OTHER',sys.executable,'-I',driver],
        ['timeout','2','-s','TERM',sys.executable,'-I',driver],
        ['timeout','2','--','env','-S','python3 -I',driver],
    ]:
        result = subprocess.run([sys.executable,str(RUNNER),*command,'--','build','--release'],
                                capture_output=True,text=True,timeout=3)
        assert result.returncode==125,(command,result)
        assert 'wrapper options after positional arguments' in result.stderr,result.stderr

project=out/'project';project.mkdir()
alias=out/'project-alias';alias.symlink_to(project.resolve(),target_is_directory=True)
assert runner.engine_project(['/tmp/unbound','run','-p',str(project)])==runner.engine_project(
    ['timeout','5','/tmp/unbound','run','--project='+str(alias)])
fake=out/'unbound'
fake.write_text('#!/usr/bin/env bash\nset -euo pipefail\nexport TEST_ENGINE_PROJECT="$3"\nshift 3\nexec '+
               shlex.quote(sys.executable)+' '+shlex.quote(str(Path(__file__).resolve()))+' "$@"\n')
fake.chmod(0o755)

def spawn(kind, name, seconds=.4, project=None, priority=False):
    handle=(out/(name+'.log')).open('w')
    command=([str(fake),'run','-p',str(project)] if project else [sys.executable,__file__])
    env = dict(os.environ)
    env.pop(runner.LAND_PRIORITY, None)
    if priority:
        env[runner.LAND_PRIORITY] = '1'
    process=subprocess.Popen([sys.executable,str(RUNNER),'--kind',kind,*command,
                              'worker',str(events),name,str(seconds),kind],stdout=handle,stderr=subprocess.STDOUT,
                              env=env)
    return process,handle


def join(jobs):
    for process,handle in jobs:
        assert process.wait(timeout=25)==0,handle.name
        handle.close()


def wait_for_log(name, text):
    deadline=time.monotonic()+10
    while time.monotonic()<deadline:
        contents=(out/(name+'.log')).read_text()
        if text in contents:
            return contents
        time.sleep(.05)
    raise AssertionError((name,text,contents))


def wait_for_priority(kind, active=True):
    deadline=time.monotonic()+10
    while time.monotonic()<deadline:
        if runner.pending_land_job(kind,runner.policy()) == active:
            return
        time.sleep(.05)
    raise AssertionError((kind,'priority notice did not change',active))


# An ordinary job already waiting must observe a later gate notice and leave the
# next released slot to that gate. Waiting builds must not stop unrelated pools.
for kind, pool, count in [('build','build',runner.policy()['build_slots']),
                          ('test','test',runner.policy()['test_slots']),
                          ('engine-cpu','engine',runner.policy()['engine_slots']),
                          ('tool','tool',runner.policy()['tool_slots'])]:
    handles=[]
    for number in range(count):
        handle=(state/f'parity-{pool}-slot-{number}.lock').open('a')
        fcntl.flock(handle,fcntl.LOCK_EX)
        handles.append(handle)
    try:
        ordinary=spawn(kind,'priority-ordinary-'+pool,.1)
        wait_for_log('priority-ordinary-'+pool,'waiting for '+kind)
        gate=spawn(kind,'priority-gate-'+pool,.3,priority=True)
        wait_for_priority(kind)
        # The marker is not a held admission token or an exclusive entry lock.
        deadline=time.monotonic()+5
        while True:
            try:
                with runner.lock('parity-heavy-entry.lock',fcntl.LOCK_EX|fcntl.LOCK_NB):
                    break
            except BlockingIOError:
                assert time.monotonic()<deadline,'waiting gate pinned the entry turnstile'
                time.sleep(.01)
        independent=spawn('editor-cpu','priority-independent-'+pool,.1)
        join([independent])
        handles.pop().close()  # only one slot is free for the two contenders
        join([gate,ordinary])
    finally:
        for handle in handles:
            handle.close()
    records=[json.loads(line) for line in events.read_text().splitlines()]
    gate_end=next(r['time'] for r in records if r['name']=='priority-gate-'+pool and r['phase']=='end')
    ordinary_start=next(r['time'] for r in records if r['name']=='priority-ordinary-'+pool and r['phase']=='start')
    assert gate_end<=ordinary_start,('ordinary job bypassed gate',kind)

# GPU notices compete with both graphics pools, while no engine admission token
# is held before the queued gate has a GPU slot too.
handles=[]
for name in runner.GPU_SLOT_NAMES[:runner.policy()['gpu_slots']]:
    handle=(state/name).open('a')
    fcntl.flock(handle,fcntl.LOCK_EX)
    handles.append(handle)
try:
    ordinary=spawn('editor-gpu','priority-ordinary-gpu',.1)
    wait_for_log('priority-ordinary-gpu','waiting for editor-gpu')
    gate=spawn('engine-gpu','priority-gate-gpu',.3,priority=True)
    wait_for_priority('editor-gpu')
    unpinned=runner.attempt('engine-cpu',runner.policy())
    assert unpinned is not None,'waiting GPU gate pinned an engine slot'
    unpinned.close()
    join([spawn('editor-cpu','priority-independent-gpu',.1)])
    handles.pop().close()
    join([gate,ordinary])
finally:
    for handle in handles:
        handle.close()
records=[json.loads(line) for line in events.read_text().splitlines()]
gate_end=next(r['time'] for r in records if r['name']=='priority-gate-gpu' and r['phase']=='end')
ordinary_start=next(r['time'] for r in records if r['name']=='priority-ordinary-gpu' and r['phase']=='start')
assert gate_end<=ordinary_start,'graphics Editor bypassed the waiting gate engine'

# An ordinary exclusive writer must relinquish its turnstile when a gate arrives,
# even while it is waiting for an older running reader to drain.
with runner.lock('parity-heavy-job.lock',fcntl.LOCK_SH):
    writer=spawn('heavy','priority-writer',.1)
    wait_for_log('priority-writer',f'pid {os.getpid()}, cmd=')
    gate=spawn('build','priority-before-writer',.1,priority=True)
    join([gate])
join([writer])

# Cancellation of a queued gate cannot leave a stale priority notice blocking jobs.
handles=[]
for number in range(runner.policy()['build_slots']):
    handle=(state/f'parity-build-slot-{number}.lock').open('a')
    fcntl.flock(handle,fcntl.LOCK_EX)
    handles.append(handle)
try:
    gate,log=spawn('build','priority-cancelled',.1,priority=True)
    wait_for_priority('build')
    gate.terminate()
    assert gate.wait(timeout=10)!=0
    log.close()
    wait_for_priority('build',active=False)
    handles.pop().close()
    join([spawn('build','priority-after-cancel',.1)])
finally:
    for handle in handles:
        handle.close()

# Weighted-memory policies define weights for pooled kinds, never for exclusive
# writers. All kinds must still admit, and an ordinary writer must yield to a
# weighted gate without querying a nonexistent heavy-job memory weight.
original_policy=(state/'job-limits.json').read_text()
weighted=json.loads(original_policy)
weighted['memory_tokens']=8
weighted['weights']={kind:1 for kind in runner.KINDS}
(state/'job-limits.json').write_text(json.dumps(weighted))
try:
    assert not runner.pending_land_job('build',runner.policy())
    assert not runner.pending_land_job('heavy',runner.policy())
    for kind in [*runner.KINDS,'heavy']:
        join([spawn(kind,'weighted-'+kind,.05)])
    handles=[]
    for number in range(weighted['build_slots']):
        handle=(state/f'parity-build-slot-{number}.lock').open('a')
        fcntl.flock(handle,fcntl.LOCK_EX)
        handles.append(handle)
    try:
        gate=spawn('build','weighted-priority-gate',.1,priority=True)
        wait_for_priority('heavy')
        writer=spawn('heavy','weighted-priority-writer',.1)
        handles.pop().close()
        join([gate,writer])
    finally:
        for handle in handles:
            handle.close()
finally:
    (state/'job-limits.json').write_text(original_policy)

# A raw shared lock stands in for an older runner. The waiting writer must name
# that holder, keep the turnstile, and be named by the reader queued behind it.
with runner.lock('parity-heavy-job.lock',fcntl.LOCK_SH):
    writer=spawn('heavy','diagnostic-writer',.2)
    message=wait_for_log('diagnostic-writer',f'pid {os.getpid()}, cmd=')
    assert str(Path(__file__).name) in message,message
    assert 'cwd='+os.getcwd() in message,message
    # The writer retains the entry lock while waiting, preserving writer priority.
    try:
        with runner.lock('parity-heavy-entry.lock',fcntl.LOCK_EX|fcntl.LOCK_NB):
            raise AssertionError('waiting writer released the entry lock')
    except BlockingIOError:
        pass
    reader=spawn('editor-cpu','diagnostic-reader',.1)
    message=wait_for_log('diagnostic-reader','parity-heavy-entry.lock:')
    assert 'diagnostic-writer' in message,message
    assert 'cwd='+os.getcwd() in message,message
join([writer,reader])
diagnostics=[json.loads(line) for line in events.read_text().splitlines()]
writer_end=next(row['time'] for row in diagnostics if row['name']=='diagnostic-writer' and row['phase']=='end')
reader_start=next(row['time'] for row in diagnostics if row['name']=='diagnostic-reader' and row['phase']=='start')
assert writer_end<=reader_start,'reader bypassed the waiting writer'

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
jobs=[spawn('build','build-'+str(i),.6) for i in range(config['build_slots']+1)]
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
    mixed_overlap=mixed_overlap,no_head_of_line_blocking=True,land_priority=True,
    combined_memory_max_bytes=16*2**30,engine_memory_max_bytes=8*2**30,
    engine_job_memory_max_bytes=4*2**30,swap_allowed=True,exclusive_builds=False,build_slots=config['build_slots'],
    build_overlap=build_overlap,project_serial=True,cancellation=True),indent=2)+'\n')
print('Editor/engine/build/GPU slots, gate priority, no head-of-line blocking, exclusive writer and cancellation verified;',
      'Editor peak',peak,'engine peak',engine_peak,'GPU peak',gpu_peak)
