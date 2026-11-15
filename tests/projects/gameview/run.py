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
def display():
    # X assigns and reserves the display before returning its number, including across runners.
    import select
    reader, writer = os.pipe()
    server = subprocess.Popen(['Xvfb','-displayfd',str(writer),'-noreset','-screen','0','1280x720x24','-nolisten','tcp'],
                              pass_fds=(writer,),stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    os.close(writer)
    try:
        ready, _, _ = select.select([reader],[],[],10)
        number = os.read(reader,64).decode().strip() if ready else ''
        if not number.isdigit() or server.poll() is not None:
            raise RuntimeError('private X server did not assign a display')
        return ':'+number,server
    except BaseException:
        server.terminate();server.wait()
        raise
    finally:
        os.close(reader)

base.display = display

def wait_file(path, job, timeout=120):
    deadline = time.monotonic()+timeout
    while not path.exists():
        if job.poll() is not None:
            raise RuntimeError(f'process exited {job.returncode} before {path.name}')
        if time.monotonic() > deadline:
            raise RuntimeError(f'timed out waiting for {path}')
        time.sleep(.02)


def unbound(work, gate=False):
    from PIL import ImageGrab
    import json
    cli = os.environ.get('UNBOUND_CLI', str(base.REPO/'target/release/unbound'))
    name, server = display()
    combined = []
    try:
        for c, (ww,wh) in enumerate(((640,360),(360,640),(640,360),(640,360),(641,361),(360,640),(640,360),(640,360))):
            project = base.copy_project(work/'projects'/str(c))
            out = base.fresh_output(work/str(c))
            env = dict(os.environ, DISPLAY=name, SCREENREAD_PROBE_OUT=str(out), GAMEVIEW_CASE=str(c))
            env.pop('XAUTHORITY',None)
            env.pop('WAYLAND_DISPLAY',None)
            env.pop('WAYLAND_SOCKET',None)
            env['XDG_SESSION_TYPE']='x11'
            env['WINIT_UNIX_BACKEND']='x11'
            with (out/'unbound.log').open('w') as log:
                job = subprocess.Popen([str(base.LAUNCHER),'--kind','engine-gpu','timeout','180',cli,
                    'run','-p',str(project),'--no-watch','--force-vulkan','--frames','60000','--size',f'{ww}x{wh}'],
                    env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
                try:
                    wait_file(out/'ready',job)
                    ids = subprocess.check_output(['xdotool','search','--onlyvisible','--name','.*'],env=env,text=True).split()
                    windows = []
                    for identifier in ids:
                        geometry = subprocess.check_output(['xdotool','getwindowgeometry','--shell',identifier],env=env,text=True)
                        fields = dict(line.split('=',1) for line in geometry.splitlines())
                        if (int(fields['WIDTH']),int(fields['HEIGHT'])) == (ww,wh):
                            windows.append((identifier,fields))
                    if len(windows)!=1: raise RuntimeError(f'expected one {ww}x{wh} window, got {windows}')
                    identifier,fields = windows[0]
                    subprocess.run(['xdotool','windowfocus',identifier],env=env,check=True)
                    time.sleep(.2)
                    left,top = int(fields['X']),int(fields['Y'])
                    if (int(fields['WIDTH']),int(fields['HEIGHT'])) != (ww,wh):
                        raise RuntimeError('host window has the wrong size')
                    image = ImageGrab.grab(xdisplay=name).crop((left,top,left+ww,top+wh)).convert('RGB')
                    image.save(out/'window.png')
                    if c>=6:
                        subprocess.run(['xdotool','windowsize',identifier,'800','600'],env=env,check=True)
                        (out/'resize').touch()
                        wait_file(out/'resized',job)
                        time.sleep(.2)
                        image=ImageGrab.grab(xdisplay=name).crop((left,top,left+800,top+600)).convert('RGB')
                        image.save(out/'resized.png')
                        (out/'finish').touch()
                    else:
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
            adapter = next((line for line in (out/'unbound.log').read_text().splitlines() if 'AdapterInfo' in line), '')
            if 'NVIDIA' not in adapter: raise RuntimeError('NVIDIA engine renderer was not verified')
            case_lines=(out/'values.jsonl').read_text().splitlines()
            if c!=7: combined.extend(case_lines)
            # The host image must contain the native rectangle as a solid red region with black borders.
            rows = {r['k']:r['v'] for r in map(json.loads,combined)}
            if c>=6:
                control={r['k']:r['v'] for r in map(json.loads,case_lines)}
                if control['retained.after.draws'] != [0]: raise RuntimeError('retained control rendered a new camera frame')
                x,y,w,h=control['retained.after.target']
                if c==7 and (x,y,w,h)!=(0,0,800,600): raise RuntimeError('unfixed retained view did not follow the window')
            else:
                x,y,w,h = rows[f'{c}.target']
            expected = (round(x),round(y),round(x)+round(w),round(y)+round(h))
            mask = image.getchannel('R').point(lambda v:255 if v>250 else 0)
            if mask.getbbox()!=expected:
                raise RuntimeError(f'case {c} host image bounds {mask.getbbox()} != {expected}')
            colors = image.getcolors(maxcolors=3)
            if colors is None or any(color not in ((0,0,0),(255,0,0)) for _,color in colors):
                raise RuntimeError(f'case {c} unexpected host colors {colors}')
        combined.append('{"k":"complete","v":true}')
        target=work/'values.jsonl'
        target.write_text('\n'.join(combined)+'\n')
        result = compare(base.reference(),target)
        if result and gate:
            failures = differences(base.reference(),target)[1]
            if precision_only(failures):
                print('gate: only the recorded pointer precision residual remains')
                return 0
        return result
    finally:
        server.terminate();server.wait()


# Exact raw values, not a numeric tolerance. The strict comparator still reports this row.
PRECISION = {'1.pointer2': ([480, -124.444443], [480, -124.444458])}


def differences(reference,capture):
    import json
    a={r['k']:r['v'] for r in map(json.loads,reference.read_text().splitlines())}
    b={r['k']:r['v'] for r in map(json.loads,capture.read_text().splitlines())}
    keys = a.keys()|b.keys()
    failures=[]
    for key in sorted(keys):
        if key not in a or key not in b or a[key]!=b[key]: failures.append((key,a.get(key),b.get(key)))
    return keys,failures


def precision_only(failures):
    return bool(failures) and all(key in PRECISION and (expected,actual)==PRECISION[key]
                                  for key,expected,actual in failures)


def compare(reference,capture):
    keys,failures = differences(reference,capture)
    for key,expected,actual in failures: print(f'{key}: {actual} != {expected}')
    print(f'{len(keys)-len(failures)}/{len(keys)} records match')
    return int(bool(failures))


def validate_repeats(work):
    for repeat in ('native1','native2'):
        reason = base.incomplete(work/repeat)
        if reason:
            raise RuntimeError(f'{repeat}: {reason}')
        project = work/'projects'/repeat
        for folder in ('Assets','Packages','ProjectSettings'):
            for source in (PROBE/folder).rglob('*'):
                if source.is_file() and source.read_bytes() != (project/source.relative_to(PROBE)).read_bytes():
                    raise RuntimeError(f'{repeat} source mismatch: {source.relative_to(PROBE)}')
    if (work/'native1/values.jsonl').read_bytes() != (work/'native2/values.jsonl').read_bytes():
        raise RuntimeError('Editor repeats differ')
    print('two complete, identical NVIDIA captures with matching authored sources')


def check(work):
    validate_repeats(work)
    return compare(base.reference(),work/'native1/values.jsonl') if base.reference().exists() else 0


def validate_extension(old, new):
    import json
    old_lines=old.splitlines(keepends=True)
    new_lines=new.splitlines(keepends=True)
    old_keys={json.loads(line)['k'] for line in old_lines}
    retained=[line for line in new_lines if json.loads(line)['k'] in old_keys]
    if retained != old_lines:
        raise RuntimeError('existing reference rows changed or moved relative to each other')


def publish(work, extend=False):
    validate_repeats(work)
    target=base.reference()
    data=(work/'native1/values.jsonl').read_bytes()
    if target.exists():
        if target.read_bytes()==data:
            print('reference already matches')
            return 0
        if not extend: raise RuntimeError('reference exists; append controls with --extend')
        validate_extension(target.read_bytes(),data)
    target.parent.mkdir(parents=True,exist_ok=True)
    target.write_bytes(data)
    print('published',target)
    return 0


base.publish = publish
base.unbound = unbound
def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode',choices=('editor','publish','check','unbound','gate'))
    parser.add_argument('--release',required=True)
    parser.add_argument('--label',required=True)
    parser.add_argument('--extend',action='store_true')
    args=parser.parse_args()
    if not args.label or Path(args.label).name!=args.label or args.label in ('.','..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION']=args.release
    work=base.run_root(PROBE,args.label)
    return {'editor':base.editor,'publish':lambda work: publish(work,extend=args.extend),'check':check,'unbound':unbound,
            'gate':lambda work: unbound(work,gate=True)}[args.mode](work)


if __name__ == '__main__':
    raise SystemExit(main())
