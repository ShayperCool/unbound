#!/usr/bin/env python3
"""Bounded Editor/engine pools; builds retain exclusive heavy-job access."""
import contextlib
import fcntl
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import uuid

CACHE = Path.home()/'.cache/compat'
CONFIG = CACHE/'job-limits.json'


def policy():
    value = json.loads(CONFIG.read_text())
    assert 1 <= value['editor_slots'] <= 64
    assert 1 <= value['engine_slots'] <= 2
    assert 1 <= value['engine_slot_weight'] <= value['editor_slots']
    assert value['memory_max_bytes'] == 16*2**30
    assert value['engine_memory_max_bytes'] == 8*2**30
    assert value['engine_job_memory_max_bytes'] == 4*2**30
    return value


def group_path(slice_name):
    return Path('/sys/fs/cgroup/user.slice')/('user-'+str(os.getuid())+'.slice')/(
        'user@'+str(os.getuid())+'.service')/'unbound.slice'/slice_name


@contextlib.contextmanager
def lock(name, mode=fcntl.LOCK_EX):
    CACHE.mkdir(parents=True, exist_ok=True)
    with (CACHE/name).open('a') as handle:
        fcntl.flock(handle, mode)
        try:
            yield
        finally:
            fcntl.flock(handle, fcntl.LOCK_UN)


def slot(count, prefix='parity-editor-slot-', weight=1):
    while True:
        # Reserve all tokens together. Keeping a partial reservation while
        # waiting can deadlock two jobs that each need multiple tokens.
        with contextlib.ExitStack() as attempt:
            acquired = 0
            for index in range(count):
                handle = (CACHE/(prefix+str(index)+'.lock')).open('a')
                try:
                    fcntl.flock(handle, fcntl.LOCK_EX|fcntl.LOCK_NB)
                except BlockingIOError:
                    handle.close()
                    continue
                attempt.enter_context(handle)
                acquired += 1
                if acquired == weight:
                    return attempt.pop_all()
        time.sleep(.05)


def run(command):
    process = subprocess.Popen(command, close_fds=True)
    prior = {}
    for sig in [signal.SIGINT, signal.SIGTERM, signal.SIGHUP]:
        prior[sig] = signal.signal(sig, lambda number, frame: process.send_signal(number)
                                   if process.poll() is None else None)
    try:
        return process.wait()
    finally:
        for sig, handler in prior.items():
            signal.signal(sig, handler)


def clean_idle_compilers(config):
    # A waiting writer may hold the turnstile: never block on it while exiting.
    try:
        with lock('parity-heavy-entry.lock', fcntl.LOCK_EX|fcntl.LOCK_NB):
            held = []
            try:
                for index in range(config['editor_slots']):
                    handle = (CACHE/('parity-editor-slot-'+str(index)+'.lock')).open('a')
                    try:
                        fcntl.flock(handle, fcntl.LOCK_EX|fcntl.LOCK_NB)
                    except BlockingIOError:
                        handle.close()
                        return
                    held.append(handle)
                # These scopes belong to this runner. Other users' compilers and
                # the shared Unity licensing service are never selected.
                for path in group_path(config['slice']).glob('unbound-editor-*.scope/cgroup.procs'):
                    try:
                        pids = path.read_text().split()
                    except FileNotFoundError:
                        continue
                    for pid in pids:
                        try:
                            cmd = (Path('/proc')/pid/'cmdline').read_bytes()
                            if b'/Editor/Data/DotNetSdkRoslyn/VBCSCompiler.dll' not in cmd:
                                continue
                            fd = os.pidfd_open(int(pid))
                            try:
                                signal.pidfd_send_signal(fd, signal.SIGTERM)
                            finally:
                                os.close(fd)
                        except (FileNotFoundError, ProcessLookupError):
                            pass
            finally:
                for handle in held:
                    handle.close()
    except BlockingIOError:
        pass


def execute(command, kind, config):
    editor = kind.startswith('editor-')
    engine = kind.startswith('engine-')
    pooled = editor or engine
    with contextlib.ExitStack() as held:
        # A writer keeps the entry lock while waiting, preventing endless new
        # readers from joining the current Editor batch ahead of that writer.
        with lock('parity-heavy-entry.lock'):
            held.enter_context(lock('parity-heavy-job.lock',fcntl.LOCK_SH if pooled else fcntl.LOCK_EX))
            if pooled:
                held.enter_context(slot(config['editor_slots'],
                    weight=config['engine_slot_weight'] if engine else 1))
            if engine:
                held.enter_context(slot(config['engine_slots'],prefix='parity-engine-slot-'))
        if kind.endswith('-gpu'):
            held.enter_context(lock('parity-editor-gpu.lock'))
        result = run(command)
    if editor:
        clean_idle_compilers(config)
    return result


def classify(command):
    # Inspect the payload of known wrappers only. An executable passed as a
    # script argument does not make that whole script a pooled engine job.
    args = list(command)
    while args:
        name = Path(args[0]).name
        if name in ['time', 'timeout', 'env']:
            args.pop(0)
            while args:
                arg = args[0]
                if arg == '--':
                    args.pop(0)
                    break
                if name == 'env' and '=' in arg and not arg.startswith('-'):
                    args.pop(0)
                elif arg in ['-o', '--output', '-f', '--format', '-s', '--signal',
                             '-k', '--kill-after', '-u', '--unset', '-C', '--chdir']:
                    del args[:2]
                elif arg.startswith('-'):
                    args.pop(0)
                else:
                    break
            if name == 'timeout' and args:
                args.pop(0)  # duration
            continue
        if name == 'unbound' and len(args)>1 and args[1]=='run':
            return 'engine-cpu' if '--headless' in args[2:] else 'engine-gpu'
        break
    # Keep support for Editor debugging through gdb --args.
    if any(Path(arg).name=='Unity' and Path(arg).parent.name=='Editor' for arg in command):
        return 'editor-cpu' if '-nographics' in command else 'editor-gpu'
    return 'heavy'


def main(args):
    config = policy()
    if args==['--status']:
        result = dict(config)
        for label, path in [('editor', group_path(config['slice'])),
                            ('engine', group_path(config['engine_slice'])),
                            ('total', group_path(config['slice']).parent)]:
            result[label] = {}
            for name in ['memory.current','memory.peak','memory.max','memory.high','memory.swap.current','memory.swap.max']:
                item = path/name
                result[label][name] = item.read_text().strip() if item.exists() else None
        # Preserve the original status keys for existing consumers.
        result.update(result['editor'])
        print(json.dumps(result,indent=2))
        return 0
    kind = None
    worker = False
    scope_id = uuid.uuid4().hex
    while args and args[0] in ['--kind','--scope-id','--pool-worker']:
        option, value, *args = args
        if option=='--scope-id':
            assert re.fullmatch('[a-f0-9]{32}',value), 'invalid scope id'
            scope_id = value
        else:
            kind = value
            worker = option=='--pool-worker'
    if args and args[0]=='--':
        args = args[1:]
    if not args:
        raise SystemExit('usage: with-heavy-job [--kind editor-cpu|editor-gpu|engine-cpu|engine-gpu|heavy] COMMAND...')
    if kind is None:
        kind = classify(args)
    assert kind in ['editor-cpu','editor-gpu','engine-cpu','engine-gpu','heavy'], 'invalid job kind'
    if kind!='heavy' and not worker:
        editor = kind.startswith('editor-')
        unit = ('unbound-editor-' if editor else 'unbound-engine-')+scope_id+'.scope'
        limits = [] if editor else [
            '--property=MemoryHigh='+str(config['engine_job_memory_max_bytes']*3//4),
            '--property=MemoryMax='+str(config['engine_job_memory_max_bytes']),
            '--property=MemorySwapMax=0']
        return run(['systemd-run','--user','--scope','--quiet',
                    '--slice='+config['slice' if editor else 'engine_slice'],
                    '--unit='+unit,*limits,sys.executable,str(Path(__file__).resolve()),
                    '--pool-worker',kind,'--',*args])
    return execute(args,kind,config)


if __name__=='__main__':
    try:
        code = main(sys.argv[1:])
    except (OSError,ValueError,AssertionError) as error:
        print('with-heavy-job:',error,file=sys.stderr)
        code = 125
    raise SystemExit(code if code>=0 else 128-code)
