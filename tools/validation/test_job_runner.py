#!/usr/bin/env python3
"""Integration checks for the installed machine-wide job runner."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time

RUNNER = Path.home()/'.cache/compat/with-heavy-job'

if len(sys.argv)>1 and sys.argv[1]=='worker':
    output, name, seconds = Path(sys.argv[2]),sys.argv[3],float(sys.argv[4])
    fd = os.open(output,os.O_WRONLY|os.O_CREAT|os.O_APPEND,0o600)
    for phase in ['start','end']:
        os.write(fd,(json.dumps(dict(name=name,phase=phase,time=time.monotonic(),pid=os.getpid()))+'\n').encode())
        if phase=='start':
            time.sleep(seconds)
    os.close(fd)
    raise SystemExit(0)

out = Path(sys.argv[1])
out.mkdir(parents=True)
events = out/'events.jsonl'


def spawn(kind, name, seconds=.4):
    handle=(out/(name+'.log')).open('w')
    process=subprocess.Popen([str(RUNNER),'--kind',kind,sys.executable,__file__,
                              'worker',str(events),name,str(seconds)],stdout=handle,stderr=subprocess.STDOUT)
    return process,handle


def join(jobs):
    for process,handle in jobs:
        assert process.wait(timeout=25)==0,handle.name
        handle.close()


join([spawn('editor-cpu','cpu-'+str(i)) for i in range(10)])
jobs=[spawn('editor-cpu','reader-'+str(i)) for i in range(3)]
time.sleep(.2)
jobs.append(spawn('heavy','writer',.3))
jobs.append(spawn('editor-cpu','late-reader',.2))
join(jobs)
join([spawn('editor-gpu','gpu-'+str(i),.2) for i in range(3)])
rows=[json.loads(line) for line in events.read_text().splitlines()]
active=set()
peak=0
for row in sorted(rows,key=lambda r:r['time']):
    name=row['name']
    if row['phase']=='start':
        if name=='writer':
            assert not active,('writer overlaps',active)
        else:
            assert 'writer' not in active,('Editor overlaps writer',name)
        if name.startswith('gpu-'):
            assert not any(n.startswith('gpu-') for n in active)
        active.add(name)
        if name.startswith('cpu-'):
            peak=max(peak,sum(n.startswith('cpu-') for n in active))
            assert peak<=8,peak
    else:
        assert name in active,name
        active.remove(name)
assert not active
assert peak>=2,'Editor jobs were still serialized'

process,handle=spawn('editor-cpu','cancel',20)
deadline=time.monotonic()+10
pid=None
while time.monotonic()<deadline:
    for line in events.read_text().splitlines():
        row=json.loads(line)
        if row['name']=='cancel' and row['phase']=='start':
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
join([spawn('heavy','after-cancel',.05)])
(out/'summary.json').write_text(json.dumps(dict(editor_peak=peak,slot_limit=8,
    exclusive_builds=True,gpu_serial=True,cancellation=True),indent=2)+'\n')
print('Editor slots, exclusive writer, GPU mutex and cancellation verified; peak',peak)
