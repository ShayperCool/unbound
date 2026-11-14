#!/usr/bin/env python3
"""Measure fixed-resolution Game view presentation on an isolated NVIDIA X display."""
import argparse
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('screenread_runner', PROBE.parent/'screenread/run.py')
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
base.PROBE = PROBE
original_display = base.display

def display():
    name, server = original_display()
    for _ in range(100):
        if server.poll() is not None:
            raise RuntimeError('private X server exited')
        if subprocess.run(['xdpyinfo', '-display', name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0:
            return name, server
        time.sleep(.05)
    server.terminate()
    server.wait()
    raise RuntimeError('private X display did not become ready')

base.display = display

def wait_file(path, job, timeout=120):
    deadline = time.monotonic()+timeout
    while not path.exists():
        if job.poll() is not None:
            raise RuntimeError(f'process exited {job.returncode} before {path.name}')
        if time.monotonic() > deadline:
            raise RuntimeError(f'timed out waiting for {path}')
        time.sleep(.02)


def unbound(work):
    from PIL import ImageGrab
    import json
    cli = os.environ.get('UNBOUND_CLI', str(base.REPO/'target/release/unbound'))
    name, server = display()
    combined = []
    try:
        for c, (ww,wh) in enumerate(((640,360),(360,640),(640,360),(640,360),(641,361),(360,640))):
            project = base.copy_project(work/'projects'/str(c))
            out = base.fresh_output(work/str(c))
            env = dict(os.environ, DISPLAY=name, SCREENREAD_PROBE_OUT=str(out), GAMEVIEW_CASE=str(c))
            env.pop('XAUTHORITY',None)
            with (out/'unbound.log').open('w') as log:
                job = subprocess.Popen([str(base.LAUNCHER),'--kind','engine-gpu','timeout','180',cli,
                    'run','-p',str(project),'--no-watch','--frames','60000','--size',f'{ww}x{wh}'],
                    env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
                try:
                    wait_file(out/'ready',job)
                    ids = subprocess.check_output(['xdotool','search','--onlyvisible','--name','^Unbound$'],env=env,text=True).split()
                    if len(ids)!=1: raise RuntimeError(f'expected one window, got {ids}')
                    subprocess.run(['xdotool','windowfocus',ids[0]],env=env,check=True)
                    time.sleep(.2)
                    geometry = subprocess.check_output(['xdotool','getwindowgeometry','--shell',ids[0]],env=env,text=True)
                    fields = dict(line.split('=',1) for line in geometry.splitlines())
                    left,top = int(fields['X']),int(fields['Y'])
                    if (int(fields['WIDTH']),int(fields['HEIGHT'])) != (ww,wh):
                        raise RuntimeError('host window has the wrong size')
                    image = ImageGrab.grab(xdisplay=name).crop((left,top,left+ww,top+wh)).convert('RGB')
                    image.save(out/'window.png')
                    for point in range(3):
                        x=left+ww*(point+1)//4; y=top+wh*(point+1)//4
                        subprocess.run(['xdotool','mousemove','--sync',str(x),str(y)],env=env,check=True)
                        time.sleep(.15)
                        (out/f'move{point}').touch()
                        wait_file(out/f'read{point}',job)
                    job.wait(timeout=30)
                    if job.returncode: raise RuntimeError(f'engine exit {job.returncode}')
                finally:
                    if job.poll() is None:
                        import signal
                        os.killpg(job.pid,signal.SIGTERM)
                        try: job.wait(timeout=10)
                        except subprocess.TimeoutExpired:
                            os.killpg(job.pid,signal.SIGKILL)
                            job.wait()
            combined.extend((out/'values.jsonl').read_text().splitlines())
            # The host image must contain the native rectangle as a solid red region with black borders.
            rows = {r['k']:r['v'] for r in map(json.loads,combined)}
            x,y,w,h = rows[f'{c}.target']
            expected = (round(x),round(y),round(x)+round(w),round(y)+round(h))
            mask = image.getchannel('R').point(lambda v:255 if v>250 else 0)
            if mask.getbbox()!=expected:
                raise RuntimeError(f'case {c} host image bounds {mask.getbbox()} != {expected}')
            for pixel in image.getdata():
                if pixel not in ((0,0,0),(255,0,0)):
                    raise RuntimeError(f'case {c} unexpected host pixel {pixel}')
        combined.append('{"k":"complete","v":true}')
        target=work/'values.jsonl'
        target.write_text('\n'.join(combined)+'\n')
        return compare(base.reference(),target)
    finally:
        server.terminate();server.wait()


def compare(reference,capture):
    import json
    a={r['k']:r['v'] for r in map(json.loads,reference.read_text().splitlines())}
    b={r['k']:r['v'] for r in map(json.loads,capture.read_text().splitlines())}
    failures=[]
    for key in sorted(a.keys()|b.keys()):
        if a.get(key)!=b.get(key): failures.append((key,a.get(key),b.get(key)))
    for key,expected,actual in failures: print(f'{key}: {actual} != {expected}')
    print(f'{len(a.keys()|b.keys())-len(failures)}/{len(a.keys()|b.keys())} records match')
    return int(bool(failures))


def check(work):
    for repeat in ('native1','native2'):
        reason = base.incomplete(work/repeat)
        if reason:
            raise RuntimeError(f'{repeat}: {reason}')
    if (work/'native1/values.jsonl').read_bytes() != (work/'native2/values.jsonl').read_bytes():
        raise RuntimeError('Editor repeats differ')
    print('two complete, identical NVIDIA captures')
    return 0

base.check = check
original_publish = base.publish

def publish(work):
    check(work)
    for repeat in ('native1','native2'):
        project = work/'projects'/repeat
        for folder in ('Assets','Packages','ProjectSettings'):
            for source in (PROBE/folder).rglob('*'):
                if source.is_file() and source.read_bytes() != (project/source.relative_to(PROBE)).read_bytes():
                    raise RuntimeError(f'{repeat} source mismatch: {source.relative_to(PROBE)}')
    return original_publish(work)

base.publish = publish
base.unbound = unbound
if __name__ == '__main__':
    raise SystemExit(base.main())
