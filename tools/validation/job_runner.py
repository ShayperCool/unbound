#!/usr/bin/env python3
"""Shared Editor slots; builds/engine jobs retain exclusive heavy-job access."""
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
    assert value['memory_max_bytes'] == 16*2**30
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


def slot(count):
    while True:
        for index in range(count):
            handle = (CACHE/('parity-editor-slot-'+str(index)+'.lock')).open('a')
            try:
                fcntl.flock(handle, fcntl.LOCK_EX|fcntl.LOCK_NB)
                return handle
            except BlockingIOError:
                handle.close()
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
    with contextlib.ExitStack() as held:
        # A writer keeps the entry lock while waiting, preventing endless new
        # readers from joining the current Editor batch ahead of that writer.
        with lock('parity-heavy-entry.lock'):
            held.enter_context(lock('parity-heavy-job.lock',fcntl.LOCK_SH if editor else fcntl.LOCK_EX))
            if editor:
                held.enter_context(slot(config['editor_slots']))
        if kind=='editor-gpu':
            held.enter_context(lock('parity-editor-gpu.lock'))
        result = run(command)
    if editor:
        clean_idle_compilers(config)
    return result


def main(args):
    config = policy()
    if args==['--status']:
        path = group_path(config['slice'])
        result = dict(config)
        for name in ['memory.current','memory.peak','memory.max','memory.high','memory.swap.current','memory.swap.max']:
            item = path/name
            result[name] = item.read_text().strip() if item.exists() else None
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
        raise SystemExit('usage: with-heavy-job [--kind editor-cpu|editor-gpu|heavy] COMMAND...')
    if kind is None:
        unity = any(Path(arg).name=='Unity' and Path(arg).parent.name=='Editor' for arg in args)
        kind = ('editor-cpu' if '-nographics' in args else 'editor-gpu') if unity else 'heavy'
    assert kind in ['editor-cpu','editor-gpu','heavy'], 'invalid job kind'
    if kind.startswith('editor-') and not worker:
        unit = 'unbound-editor-'+scope_id+'.scope'
        return run(['systemd-run','--user','--scope','--quiet','--slice='+config['slice'],
                    '--unit='+unit,sys.executable,str(Path(__file__).resolve()),
                    '--pool-worker',kind,'--',*args])
    return execute(args,kind,config)


if __name__=='__main__':
    try:
        code = main(sys.argv[1:])
    except (OSError,ValueError,AssertionError) as error:
        print('with-heavy-job:',error,file=sys.stderr)
        code = 125
    raise SystemExit(code if code>=0 else 128-code)
