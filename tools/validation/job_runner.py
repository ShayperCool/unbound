#!/usr/bin/env python3
"""Bounded Editor/engine/build pools; unknown heavy commands keep exclusive access.

Every pooled job reserves all it needs at once, without blocking while it holds
anything: Editor count slots, memory tokens (one GiB each, weighted by kind from
measured peaks), engine slots, a build slot, a test slot and a GPU slot. A job that cannot get
everything releases what it took and retries, so a waiting job never pins tokens
and never blocks other jobs whose resources are free. Only an exclusive 'heavy' job
keeps the entry lock while it waits, so newer pooled jobs queue behind it.
Every job appends its wait and run time to job-timings.jsonl."""
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import random
import re
import signal
import shlex
import subprocess
import sys
import time
import uuid

# Tests point UNBOUND_JOB_RUNNER_STATE at a private directory for locks and policy.
CACHE = Path(os.environ.get('UNBOUND_JOB_RUNNER_STATE') or Path.home()/'.cache/compat')
CONFIG = CACHE/'job-limits.json'
TIMINGS = CACHE/'job-timings.jsonl'
BUILD_TOOLS = {'cargo': {'build', 'test', 'check', 'bench', 'clippy', 'doc',
                         'rustc', 'rustdoc', 'fetch', 'metadata', 'update',
                         'generate-lockfile', 'clean', 'fix'},
               'rustc': None,
               'dotnet': {'build', 'test', 'publish', 'restore', 'msbuild', 'pack', 'format'}}


# GPU slot 0 keeps the name of the former single GPU mutex, so a job admitted by
# the previous runner still occupies one GPU slot while the new runner is rolled out.
KINDS = ['editor-cpu', 'editor-gpu', 'engine-cpu', 'engine-gpu', 'build', 'test', 'tool']
# CPU job kinds that run in the build slice with the per-build memory limits.
CPU_KINDS = ('build', 'test', 'tool')
GPU_SLOT_NAMES = ['parity-editor-gpu.lock']+['parity-gpu-slot-'+str(i)+'.lock' for i in range(1, 8)]


def policy():
    value = json.loads(CONFIG.read_text())
    assert 1 <= value['editor_slots'] <= 64
    assert value['memory_max_bytes'] == 16*2**30
    assert value['engine_memory_max_bytes'] == 8*2**30
    assert value['engine_job_memory_max_bytes'] == 4*2**30
    value.setdefault('build_slice', 'unbound-builds.slice')
    value.setdefault('build_memory_high_bytes', 7*2**30)
    value.setdefault('build_memory_max_bytes', 9*2**30)
    value.setdefault('build_slots', 1)
    value.setdefault('engine_slots', 2)
    value.setdefault('gpu_slots', 1)
    value.setdefault('tool_slots', 2)
    # Test runs (`dotnet test`) have their own slots: a long test run must not hold
    # a build slot that compilers in other worktrees are waiting for.
    value.setdefault('test_slots', 2)
    # Optional memory tokens (one GiB each, weighted by kind). Without them only
    # the count caps and the systemd slices bound the pools.
    value.setdefault('memory_tokens', 0)
    value.setdefault('weights', {})
    assert 1 <= value['engine_slots'] <= 8
    assert 1 <= value['build_slots'] <= 2
    assert 1 <= value['gpu_slots'] <= len(GPU_SLOT_NAMES)
    assert 1 <= value['tool_slots'] <= 8
    assert 1 <= value['test_slots'] <= 4
    assert 0 <= value['memory_tokens'] <= 64
    assert value['memory_tokens']*2**30 <= value['memory_max_bytes']
    if value['memory_tokens']:
        for kind in KINDS:
            assert 1 <= value['weights'][kind] <= value['memory_tokens'], kind
    return value


def record(entry):
    try:
        with TIMINGS.open('a') as handle:
            handle.write(json.dumps(entry)+'\n')
    except OSError:
        pass


def own_memory_peak():
    try:
        line = Path('/proc/self/cgroup').read_text().strip().split('\n')[-1]
        path = Path('/sys/fs/cgroup')/line.split('::',1)[1].lstrip('/')/'memory.peak'
        return int(path.read_text())
    except (OSError, ValueError, IndexError):
        return None


def group_path(slice_name):
    return Path('/sys/fs/cgroup/user.slice')/('user-'+str(os.getuid())+'.slice')/(
        'user@'+str(os.getuid())+'.service')/'unbound.slice'/slice_name


@contextlib.contextmanager
def lock(name, mode=fcntl.LOCK_EX, on_wait=None):
    CACHE.mkdir(parents=True, exist_ok=True)
    with (CACHE/name).open('a') as handle:
        if on_wait is None:
            fcntl.flock(handle, mode)
        else:
            try:
                fcntl.flock(handle, mode|fcntl.LOCK_NB)
            except BlockingIOError:
                on_wait()
                # Queue in the kernel after the diagnostic, just as older
                # runners do, rather than racing queued writers on a timer.
                fcntl.flock(handle, mode)
        try:
            yield
        finally:
            fcntl.flock(handle, fcntl.LOCK_UN)


def try_tokens(stack, names, weight):
    """Lock `weight` of the named files without blocking; False if not enough are free."""
    acquired = 0
    for name in names:
        if acquired == weight:
            break
        handle = (CACHE/name).open('a')
        try:
            fcntl.flock(handle, fcntl.LOCK_EX|fcntl.LOCK_NB)
        except BlockingIOError:
            handle.close()
            continue
        stack.enter_context(handle)
        acquired += 1
    return acquired == weight


def needs(kind, config):
    """(file names, count) pairs a job of this kind reserves together."""
    result = []
    if config['memory_tokens']:
        tokens = ['parity-memory-token-'+str(i)+'.lock' for i in range(config['memory_tokens'])]
        result.append((tokens, config['weights'][kind]))
    if kind.startswith('editor-'):
        result.append((['parity-editor-slot-'+str(i)+'.lock' for i in range(config['editor_slots'])], 1))
    if kind.startswith('engine-'):
        result.append((['parity-engine-slot-'+str(i)+'.lock' for i in range(config['engine_slots'])], 1))
    if kind == 'build':
        result.append((['parity-build-slot-'+str(i)+'.lock' for i in range(config['build_slots'])], 1))
    if kind == 'test':
        result.append((['parity-test-slot-'+str(i)+'.lock' for i in range(config['test_slots'])], 1))
    if kind == 'tool':
        result.append((['parity-tool-slot-'+str(i)+'.lock' for i in range(config['tool_slots'])], 1))
    if kind.endswith('-gpu'):
        result.append((GPU_SLOT_NAMES[:config['gpu_slots']], 1))
    return result


def attempt(kind, config):
    """Everything the job needs, or None with nothing held."""
    with contextlib.ExitStack() as stack:
        handle = (CACHE/'parity-heavy-job.lock').open('a')
        try:
            fcntl.flock(handle, (fcntl.LOCK_EX if kind == 'heavy' else fcntl.LOCK_SH)|fcntl.LOCK_NB)
        except BlockingIOError:
            handle.close()
            return None
        stack.enter_context(handle)
        if kind != 'heavy':
            for names, count in needs(kind, config):
                if not try_tokens(stack, names, count):
                    return None
        return stack.pop_all()


def blocking_holders(names):
    """Best-effort Linux lock owners, including jobs started by older runners."""
    files = {}
    for name in names:
        try:
            info = (CACHE/name).stat()
            files[(os.major(info.st_dev), os.minor(info.st_dev), info.st_ino)] = name
        except OSError:
            continue
    try:
        rows = Path('/proc/locks').read_text().splitlines()
    except OSError as error:
        return ['holder details unavailable: '+str(error)]
    result = []
    for row in rows:
        fields = row.split()
        if len(fields)<6 or fields[1]!='FLOCK':
            continue
        try:
            major, minor, inode = fields[5].split(':')
            name = files.get((int(major,16), int(minor,16), int(inode)))
            pid = int(fields[4])
            if name is None or pid == os.getpid():
                continue
            proc = Path('/proc')/str(pid)
            command = shlex.join(proc.joinpath('cmdline').read_text().rstrip('\0').split('\0'))
            cwd = os.readlink(proc/'cwd')
            result.append(f'{name}: pid {pid}, cmd={command}, cwd={cwd}')
        except (OSError, ValueError):
            # An owner can exit between the lock snapshot and reading /proc.
            continue
    return result or ['holder details unavailable (owners exited or are inaccessible)']


def admit(kind, config):
    # Only an exclusive job keeps the entry lock while it waits, so new pooled
    # jobs cannot keep bypassing it. A pooled job takes the entry lock just for
    # one attempt: a pooled waiter never delays jobs that need other slots.
    # Pooled jobs each need one slot per pool, so retrying cannot starve them.
    next_report = 0

    def report_wait():
        nonlocal next_report
        if time.monotonic() < next_report:
            return
        next_report = time.monotonic()+5
        names = ['parity-heavy-entry.lock', 'parity-heavy-job.lock']
        if kind != 'heavy':
            names += [name for group, _ in needs(kind, config) for name in group]
        print(f'with-heavy-job: waiting for {kind}; '+ '; '.join(blocking_holders(names)),
              file=sys.stderr, flush=True)

    while True:
        # Re-read the policy while waiting, so a raised limit admits queued jobs.
        try:
            config = policy()
        except (OSError, ValueError, AssertionError):
            pass
        with lock('parity-heavy-entry.lock', on_wait=report_wait):
            while True:
                held = attempt(kind, config)
                if held is not None:
                    return held
                report_wait()
                if kind != 'heavy':
                    break
                time.sleep(.05)
        # Outside the entry lock, so other jobs can be admitted meanwhile.
        time.sleep(.05+random.random()*.1)


def run(command):
    # A compiler server must not outlive its lease and keep filling a bounded
    # pool after its command has released the admission tokens.
    env = dict(os.environ, UseSharedCompilation='false', MSBUILDDISABLENODEREUSE='1',
               DOTNET_CLI_USE_MSBUILD_SERVER='0')
    process = subprocess.Popen(command, close_fds=True, env=env)
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
    # Private test locks cannot establish that the machine-wide pool is idle.
    if CACHE.resolve() != (Path.home()/'.cache/compat').resolve():
        return
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
    build = kind in CPU_KINDS
    pooled = editor or engine or build
    queued = time.time()
    with contextlib.ExitStack() as held:
        project = engine_project(command) if engine else None
        if project is not None:
            key = hashlib.sha256(str(project).encode()).hexdigest()
            # Wait before taking admission tokens, so another independent
            # project can use the engine pool while this project is busy.
            held.enter_context(lock('parity-engine-project-'+key+'.lock'))
        held.enter_context(admit(kind, config))
        started = time.time()
        result = run(command)
        finished = time.time()
    record({'kind': kind, 'start': round(queued, 1), 'wait_s': round(started-queued, 1),
            'run_s': round(finished-started, 1), 'exit': result, 'cwd': os.getcwd(),
            'cmd': ' '.join(command)[:300], 'memory_peak': own_memory_peak() if pooled else None})
    if editor:
        clean_idle_compilers(config)
    return result


def payload(command):
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
                if name == 'env' and (re.match(r'^-[iv0]*S', arg) or
                        (arg.startswith('--') and '--split-string'.startswith(arg.split('=',1)[0]))):
                    raise ValueError('env split-string wrappers are unsupported; pass expanded '
                                     'command arguments to the runner. Run tools/build.py directly '
                                     'because it manages its own build lease.')
                if name == 'env' and '=' in arg and not arg.startswith('-'):
                    args.pop(0)
                elif arg in ['-o', '--output', '-f', '--format', '-s', '--signal',
                             '-k', '--kill-after', '-u', '--unset', '-C', '--chdir', '-a', '--argv0']:
                    del args[:2]
                elif arg.startswith('-'):
                    args.pop(0)
                else:
                    break
            if name == 'timeout' and args:
                args.pop(0)  # duration
            continue
        break
    return args


def engine_project(command):
    args = payload(command)
    if len(args)<2 or Path(args[0]).name!='unbound' or args[1]!='run':
        return None
    for index,arg in enumerate(args[2:],2):
        if arg in ['-p','--project'] and index+1<len(args):
            return Path(args[index+1]).resolve()
        if arg.startswith('--project='):
            return Path(arg.split('=',1)[1]).resolve()
    return Path.cwd().resolve()


def is_build_driver(command):
    args = payload(command)
    if args and re.fullmatch(r'python(?:3(?:\.\d+)?)?', Path(args[0]).name):
        args = args[1:]
        while args and args[0].startswith('-'):
            option = args.pop(0)
            if option == '--':
                break
            if option.startswith(('-c', '-m')):
                return False
            if option in ['-W', '-X', '--check-hash-based-pycs'] and args:
                args.pop(0)
    return bool(args and Path(os.path.normpath(args[0])).parts[-2:]==('tools', 'build.py'))


def classify(command):
    args = payload(command)
    if len(args)>1 and Path(args[0]).name=='unbound' and args[1]=='run':
        headless = any(arg=='--headless' or arg.startswith('--headless=') for arg in args[2:])
        return 'engine-cpu' if headless else 'engine-gpu'
    # Keep support for Editor debugging through gdb --args.
    if any(Path(arg).name=='Unity' and Path(arg).parent.name=='Editor' for arg in command):
        return 'editor-cpu' if '-nographics' in command else 'editor-gpu'
    # Other unbound subcommands (compile, shaders, ...) and .NET tools run as
    # `dotnet Tool.dll` do CPU work without starting Editors or engine runs.
    if len(args)>1 and Path(args[0]).name=='unbound':
        return 'tool'
    if len(args)>1 and Path(args[0]).name=='dotnet' and args[1].endswith('.dll'):
        return 'tool'
    if len(args)>1 and Path(args[0]).name=='dotnet' and args[1]=='test':
        return 'test'
    if args and Path(args[0]).name in BUILD_TOOLS:
        verbs = BUILD_TOOLS[Path(args[0]).name]
        if verbs is None or (len(args)>1 and args[1] in verbs):
            return 'build'
    return 'heavy'


def main(args):
    if args==['--status']:
        config = policy()
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
        raise SystemExit('usage: with-heavy-job [--kind '+'|'.join(KINDS)+'|heavy] COMMAND...')
    if is_build_driver(args):
        print('with-heavy-job: run tools/build.py directly; it manages its own build lease. '
              'Wrapping it can deadlock the job pool. Use --direct only inside an existing '
              'build lease.', file=sys.stderr)
        return 125
    config = policy()
    if kind is None or (kind=='build' and not worker and classify(args)=='test'):
        # Callers that still pass `--kind build` for `dotnet test` get the test pool.
        kind = classify(args)
    assert kind in [*KINDS,'heavy'], 'invalid job kind'
    if kind!='heavy' and not worker:
        editor = kind.startswith('editor-')
        build = kind in CPU_KINDS
        unit = ('unbound-editor-' if editor else 'unbound-build-' if build else
                'unbound-engine-')+scope_id+'.scope'
        if editor:
            limits = []
        elif build:
            limits = ['--property=MemoryHigh='+str(config['build_memory_high_bytes']),
                      '--property=MemoryMax='+str(config['build_memory_max_bytes'])]
        else:
            limits = [
            '--property=MemoryHigh='+str(config['engine_job_memory_max_bytes']*3//4),
            '--property=MemoryMax='+str(config['engine_job_memory_max_bytes'])]
        return run(['systemd-run','--user','--scope','--quiet',
                    '--slice='+config['build_slice' if build else 'slice' if editor else 'engine_slice'],
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
