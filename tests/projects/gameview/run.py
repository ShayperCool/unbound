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
def display(log_path=None):
    # X assigns and reserves the display before returning its number, including across runners.
    import select
    import tempfile
    if log_path is None:
        directory = base.run_root(PROBE, 'displays')
        directory.mkdir(parents=True, exist_ok=True)
        descriptor, filename = tempfile.mkstemp(prefix='xserver-', suffix='.log', dir=directory)
        os.close(descriptor)
        log_path = Path(filename)
    log_path.parent.mkdir(parents=True, exist_ok=True)
    reader, writer = os.pipe()
    try:
        with log_path.open('w') as log:
            server = subprocess.Popen(['Xvfb','-displayfd',str(writer),'-noreset','-screen','0','1280x720x24','-nolisten','tcp'],
                                      pass_fds=(writer,),stdout=log,stderr=log)
    except BaseException:
        os.close(reader)
        raise
    finally:
        os.close(writer)
    server.startup_log = log_path
    try:
        deadline = time.monotonic() + 10
        # The display number and its newline can arrive in separate writes.
        assigned = b''
        while b'\n' not in assigned:
            remaining = deadline - time.monotonic()
            if remaining <= 0 or not select.select([reader], [], [], remaining)[0]:
                break
            part = os.read(reader, 64)
            if not part:
                break
            assigned += part
        number = assigned.decode().strip()
        if not assigned.endswith(b'\n') or not number.isdigit() or server.poll() is not None:
            raise RuntimeError(f'private X server did not assign a complete display number; startup log: {log_path}')
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


def unbound(work, gate=False, enabled_resize=False):
    from PIL import ImageGrab
    import json
    cli = os.environ.get('UNBOUND_CLI', str(base.REPO/'target/release/unbound'))
    name, server = display(work/'xserver.log')
    combined = []
    try:
        cases = [(c, size) for c, size in enumerate(((640,360),(360,640),(640,360),(640,360),(641,361),(360,640),(640,360),(640,360)))]
        if enabled_resize: cases = [(8, (640,360))]
        for c, (ww,wh) in cases:
            project = base.copy_project(work/'projects'/str(c))
            out = base.fresh_output(work/str(c))
            env = dict(os.environ, DISPLAY=name, SCREENREAD_PROBE_OUT=str(out), GAMEVIEW_CASE=str(c), GAMEVIEW_RESIZE_PROBE="1" if enabled_resize else "")
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
            if c!=7: combined.extend(line for line in case_lines if json.loads(line)["k"] != "complete")
            # The host image must contain the native rectangle as a solid red region with black borders.
            rows = {r['k']:r['v'] for r in map(json.loads,combined)}
            if c>=6:
                control={r['k']:r['v'] for r in map(json.loads,case_lines)}
                prefix = 'enabled' if enabled_resize else 'retained'
                if control[prefix+'.after.draws'] != [0]: raise RuntimeError('skipped-frame control rendered a new camera frame')
                x,y,w,h=control[prefix+'.after.target']
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
        reference = enabled_reference() if enabled_resize else base.reference()
        result = compare(reference,target)
        if result and gate and not enabled_resize:
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


def enabled_reference():
    reference = base.reference()
    return reference.with_name(reference.stem+'-enabled-resize.jsonl')


def enabled_editor(work):
    import editor_runner
    for repeat in ('native1', 'native2'):
        project = base.copy_project(work/'projects'/repeat)
        out = base.fresh_output(work/repeat)
        job = editor_runner.EditorJob(project, out/'editor.log', out,
            env={'SCREENREAD_PROBE_OUT': str(out), 'GAMEVIEW_RESIZE_PROBE': '1'},
            timeout=900, progress_files=[out/'values.jsonl'], progress_timeout=480,
            label='enabled-resize-'+repeat, launch_inputs={'release': os.environ['UNBOUND_PROBE_UNITY_VERSION'],
                'paths': {key: str(project/part) for key,part in
                          {'assets':'Assets','packages':'Packages','settings':'ProjectSettings'}.items()}})
        print(editor_runner.summary(editor_runner.run(job)), flush=True)
        reason = editor_runner.result_problem(out, gpu=True)
        if reason: raise RuntimeError(f'{repeat}: {reason}')
    return 0


def publish_enabled(work):
    import base64
    import math
    import hashlib
    import json
    import editor_runner
    expected = {key+'/'+str(p.relative_to(PROBE/part)): hashlib.sha256(p.read_bytes()).hexdigest()
                for key, part in {'assets':'Assets','packages':'Packages','settings':'ProjectSettings'}.items()
                for p in (PROBE/part).rglob('*') if p.is_file()}
    # The runner writes the selected release into the project copy before launching.
    expected['settings/ProjectVersion.txt'] = hashlib.sha256(
        ('m_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n').encode()).hexdigest()
    captures=[]
    keys=['enabled.before.screen','enabled.noResize.draws','enabled.noResize.renderLatches',
          'enabled.noResize.willRender','enabled.after.draws','enabled.after.renderLatches',
          'enabled.after.willRender','enabled.after.screen','enabled.after.camera','enabled.after.target',
          'enabled.after.cameraEnabled','enabled.resumed.didRender','enabled.resumed.willRender','complete']
    for repeat in ('native1','native2'):
        directory=work/repeat
        result=editor_runner.load_result(directory) or {}
        reason=editor_runner.result_problem(directory,gpu=True)
        if reason or result.get('forced_exit'):
            raise RuntimeError(f'{repeat}: {reason or "forced exit"}')
        receipt=json.loads((directory/'prelaunch-inputs.json').read_text())
        actual={key: item['sha256'] for key,item in receipt['files'].items()}
        if any(hashlib.sha256(base64.b64decode(item['bytes_base64'])).hexdigest() != item['sha256']
               for item in receipt['files'].values()):
            raise RuntimeError(f'{repeat}: invalid pre-launch source receipt')
        if actual != expected or receipt['selected_release'] != os.environ['UNBOUND_PROBE_UNITY_VERSION']:
            raise RuntimeError(f'{repeat}: pre-launch probe inputs differ from this checkout')
        data=(directory/'values.jsonl').read_bytes()
        rows=[json.loads(line) for line in data.splitlines()]
        if [row['k'] for row in rows] != keys or rows[-1]['v'] is not True:
            raise RuntimeError(f'{repeat}: incomplete enabled-camera control')
        if any(not isinstance(row['v'], list) or
               any(type(value) not in (int, float) or not math.isfinite(value) for value in row['v'])
               for row in rows[:-1]):
            raise RuntimeError(f'{repeat}: invalid enabled-camera value')
        captures.append(data)
    if captures[0] != captures[1]: raise RuntimeError('enabled-camera repeats differ')
    target=enabled_reference()
    target.parent.mkdir(parents=True,exist_ok=True)
    with target.open('xb') as stream: stream.write(captures[0])
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
    parser.add_argument('--enabled-resize',action='store_true')
    args=parser.parse_args()
    if not args.label or Path(args.label).name!=args.label or args.label in ('.','..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION']=args.release
    work=base.run_root(PROBE,args.label)
    if args.enabled_resize:
        return {'editor':enabled_editor,'publish':publish_enabled,
                'check':lambda work: compare(enabled_reference(),work/'native1/values.jsonl'),
                'unbound':lambda work: unbound(work,enabled_resize=True),
                'gate':lambda work: unbound(work,enabled_resize=True)}[args.mode](work)
    return {'editor':base.editor,'publish':lambda work: publish(work,extend=args.extend),'check':check,'unbound':unbound,
            'gate':lambda work: unbound(work,gate=True)}[args.mode](work)


if __name__ == '__main__':
    raise SystemExit(main())
