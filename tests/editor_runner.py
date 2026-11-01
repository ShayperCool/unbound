#!/usr/bin/env python3
"""Shared Unity Editor launcher for probe runners.

    from editor_runner import EditorJob, run, run_parallel, publish

A probe runner describes each Editor as an `EditorJob` and calls `run(job)` (or
`run_parallel(jobs)` for independent repeats). Each job:

* starts the release's Editor (`probe_paths.editor_path()`) through the shared heavy-job
  launcher with an explicit kind: `editor-gpu` for a graphics Editor (windowed or
  `-batchmode`) on a private Xvfb with `vglrun -d egl`, `editor-cpu` for `-batchmode
  -nographics`. The launcher admits a small supervisor (`editor_runner.py supervise`),
  which starts the Editor and stays with it, so everything below happens inside the lease;
* stops a graphics Editor as soon as its log names a renderer that is not the NVIDIA GPU;
* stops the Editor at a hard timeout (watchdog);
* stops an Editor that is still alive `shutdown_grace` seconds after its log printed the
  shutdown line (`Cleanup mono`): TERM, then KILL after `kill_grace` seconds, to every
  process in the Editor's own process group (the Editor makes itself a group leader) and
  every descendant. The shared licensing client, which an Editor may have started in its
  group, and processes of other users are never signalled;
* follows a hand-over: when a windowed Editor exits before its shutdown line and another
  Editor process for the same project copy appears, that Editor is supervised in turn;
* after the Editor exits, sweeps the Unity Hub processes started for this project copy
  (their command line names it) and the project's Roslyn servers, then again later from a
  small detached process (the Hub can come back minutes after its Editor ended);
* writes `editor-run.json` into `job.out`: exit status, forced-exit reason, renderer,
  timings, log path, every signalled process.

`publish(...)` refuses unless every repeat has an Editor result without a forced stop
(except the shutdown guard after a logged shutdown), the expected renderer, a terminal
completion marker, and byte-identical outputs across repeats; it never overwrites a target.
"""
import argparse
import ctypes
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import threading
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parent))
from probe_paths import editor_path  # noqa: E402

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
RESULT = 'editor-run.json'
SHUTDOWN_MARKERS = (b'Cleanup mono',)
# A windowed Editor logs the Hub command line it starts: '/usr/bin/unityhub' '--' '--silent' ...
HUB_MARKER = b"unityhub' '--' '--silent'"
LATE_SWEEPS = (60, 150, 300)
FORCED_PUBLISHABLE = ('shutdown-guard',)


class EditorJob:
    """One Editor run. `out` receives editor-run.json and the Editor's stdout/stderr."""

    def __init__(self, project, log, out, args=(), *, batch=False, graphics=True, env=None,
                 timeout=1800, shutdown_grace=30, kill_grace=10, follow=30, label=None,
                 screen='1280x720x24', late_sweeps=LATE_SWEEPS, editor=None, cwd=None,
                 progress_files=(), progress_timeout=None):
        self.project = Path(project)
        self.log = Path(log)
        self.out = Path(out)
        self.args = list(args)
        self.batch = batch or not graphics
        self.graphics = graphics
        self.env = dict(env or {})
        self.timeout = timeout
        self.shutdown_grace = shutdown_grace
        self.kill_grace = kill_grace
        self.follow = follow
        self.label = label or self.out.name
        self.screen = screen
        self.late_sweeps = tuple(late_sweeps)
        self.editor = Path(editor) if editor else editor_path()
        self.cwd = Path(cwd) if cwd else self.project
        self.progress_files = [str(Path(p).resolve()) for p in progress_files]
        self.progress_timeout = progress_timeout
        if progress_timeout is not None and (progress_timeout <= 0 or not self.progress_files):
            raise ValueError('progress_timeout needs progress_files and a positive timeout')

    @property
    def kind(self):
        return 'editor-gpu' if self.graphics else 'editor-cpu'

    def command(self):
        mode = (['-batchmode'] if self.batch else [])+([] if self.graphics else ['-nographics'])
        prefix = ['vglrun', '-d', 'egl'] if self.graphics else []
        return [*prefix, str(self.editor), *mode, '-projectPath', str(self.project), *self.args,
                '-logFile', str(self.log)]


# ---------------------------------------------------------------- processes

class Proc:
    __slots__ = ('pid', 'ppid', 'pgid', 'state', 'argv', 'start_ticks')

    def __init__(self, pid, ppid, pgid, state, argv, start_ticks=None):
        self.pid, self.ppid, self.pgid, self.state, self.argv = pid, ppid, pgid, state, argv
        self.start_ticks = start_ticks

    def describe(self):
        return b' '.join(self.argv).decode(errors='replace')[:200]


def read_proc(pid):
    """This user's live process `pid`, or None (gone, zombie, or someone else's)."""
    base = Path('/proc')/str(pid)
    try:
        if base.stat().st_uid != os.getuid():
            return None
        stat = (base/'stat').read_text()
        argv = (base/'cmdline').read_bytes().split(b'\0')
    except (FileNotFoundError, ProcessLookupError, PermissionError):
        return None
    rest = stat[stat.rindex(')')+2:].split()
    if rest[0] in ('Z', 'X'):
        return None
    if argv and argv[-1] == b'':
        argv.pop()
    return Proc(int(pid), int(rest[1]), int(rest[2]), rest[0], argv, int(rest[19]))


def processes():
    for entry in Path('/proc').iterdir():
        if entry.name.isdigit():
            proc = read_proc(entry.name)
            if proc is not None:
                yield proc


def is_licensing(proc):
    joined = b' '.join(proc.argv)
    return b'Licensing' in joined or b'LicenseClient' in joined


def is_editor(proc):
    return bool(proc.argv) and proc.argv[0].endswith(b'/Editor/Unity')


def is_hub(proc):
    if not proc.argv:
        return False
    name = proc.argv[0].rsplit(b'/', 1)[-1]
    if name in (b'unityhub-bin', b'unityhub'):
        return True
    return name in (b'bash', b'sh') and len(proc.argv) > 1 and proc.argv[1].endswith(b'/unityhub')


def protected():
    """This process and its ancestors: never signalled."""
    result, pid = set(), os.getpid()
    while pid > 1:
        result.add(pid)
        proc = read_proc(pid)
        if proc is None:
            break
        pid = proc.ppid
    return result


def family(root, table=None):
    """The Editor `root`, its process group when it leads one, and all their descendants,
    without the licensing client and without this process or its ancestors."""
    table = list(processes()) if table is None else table
    chosen = {p.pid for p in table if p.pid == root or (p.pgid == root and root_leads(root, table))}
    children = {}
    for p in table:
        children.setdefault(p.ppid, []).append(p.pid)
    stack = list(chosen)
    while stack:
        for child in children.get(stack.pop(), ()):
            if child not in chosen:
                chosen.add(child)
                stack.append(child)
    keep = protected()
    return [p for p in table if p.pid in chosen and p.pid not in keep and not is_licensing(p)]


def root_leads(root, table):
    """Whether `root` leads its own process group (or did: a group outlives its leader)."""
    leader = next((p for p in table if p.pid == root), None)
    return leader is None or leader.pgid == root


def hub_processes(project, table=None):
    """The Hub processes started for this project copy: Hubs whose command line names it,
    their Hub children and same-group Hub helpers, and the crash handler they name."""
    table = list(processes()) if table is None else table
    keys = path_keys(project)
    mains = [p for p in table if is_hub(p) and keys & set(p.argv)]
    groups = {p.pgid for p in mains}
    chosen = dict((p.pid, p) for p in mains)
    changed = True
    while changed:
        changed = False
        for p in table:
            if p.pid not in chosen and is_hub(p) and (p.ppid in chosen or p.pgid in groups):
                chosen[p.pid] = p
                changed = True
    handlers = set()
    for p in list(chosen.values()):
        for arg in p.argv:
            if arg.startswith(b'--crashpad-handler-pid='):
                handlers.add(int(arg.split(b'=', 1)[1] or 0))
    for p in table:
        if p.pid in handlers and p.argv and p.argv[0].endswith(b'chrome_crashpad_handler') \
                and any(b'unityhub' in arg for arg in p.argv):
            chosen[p.pid] = p
    keep = protected()
    return [p for pid, p in sorted(chosen.items())
            if pid not in keep and not is_licensing(p) and not names_other_project(p, keys)]


def path_keys(project):
    """The project path as given and resolved: the Editor passes on the path it was given."""
    return {str(project).encode(), str(Path(project).resolve()).encode()}


def names_other_project(proc, keys):
    argv = proc.argv
    return any(arg == b'-projectPath' and i+1 < len(argv) and argv[i+1] not in keys
               for i, arg in enumerate(argv))


def roslyn_processes(project, table=None):
    """Roslyn compiler servers whose working directory variable names this project copy."""
    table = list(processes()) if table is None else table
    result = []
    for p in table:
        if not any(b'VBCSCompiler' in arg for arg in p.argv[:3]):
            continue
        try:
            env = (Path('/proc')/str(p.pid)/'environ').read_bytes().split(b'\0')
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue
        if any(b'PWD='+key in env for key in path_keys(project)):
            result.append(p)
    return result


def send(proc, sig):
    """Signal `proc` only if its pid still names the same process."""
    try:
        fd = os.pidfd_open(proc.pid)
    except (ProcessLookupError, OSError):
        return False
    try:
        now = read_proc(proc.pid)
        if now is None or now.argv != proc.argv or now.start_ticks != proc.start_ticks:
            return False
        signal.pidfd_send_signal(fd, sig)
        return True
    except (ProcessLookupError, PermissionError):
        return False
    finally:
        os.close(fd)


def alive(proc):
    now = read_proc(proc.pid)
    return now is not None and now.argv == proc.argv and now.start_ticks == proc.start_ticks


def terminate(procs, grace, reap=None):
    """TERM, then KILL whatever is still alive `grace` seconds later. `reap` (a Popen) is
    polled so a direct child does not linger as a zombie. Returns what was signalled."""
    records = []
    for p in procs:
        if send(p, signal.SIGTERM):
            records.append({'pid': p.pid, 'cmd': p.describe(), 'signals': ['TERM']})
    deadline = time.monotonic()+grace
    while time.monotonic() < deadline and any(alive(p) for p in procs):
        if reap is not None:
            reap.poll()
        time.sleep(.1)
    for p in procs:
        if alive(p) and send(p, signal.SIGKILL):
            entry = next((r for r in records if r['pid'] == p.pid), None)
            if entry is None:
                entry = {'pid': p.pid, 'cmd': p.describe(), 'signals': []}
                records.append(entry)
            entry['signals'].append('KILL')
    return records


def sweep(project, grace=5):
    """Ends the Hub and Roslyn processes this project copy left behind. The Hub ignores TERM."""
    table = list(processes())
    targets = {p.pid: p for p in hub_processes(project, table)+roslyn_processes(project, table)}
    return terminate(list(targets.values()), grace)


def schedule_sweeps(project, delays, log):
    """A detached process that sweeps again after each delay (seconds after now)."""
    if not delays:
        return None
    # setsid --fork detaches the sweeper from this process and from the job's lease.
    subprocess.run(
        ['setsid', '--fork', sys.executable, str(Path(__file__).resolve()), 'sweep', '--project',
         str(project), '--delays', ','.join(str(d) for d in delays), '--log', str(log)],
        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        close_fds=True, check=True)
    return {'delays': list(delays), 'log': str(log)}


# ---------------------------------------------------------------- supervisor

class LogWatch:
    """Reads an Editor log incrementally and remembers the lines that matter."""

    def __init__(self, path):
        self.path = Path(path)
        self.offset = 0
        self.partial = b''
        self.renderer = None
        self.shutdown = None
        self.hub = None
        self.restarts = 0

    def poll(self, now):
        try:
            size = self.path.stat().st_size
        except FileNotFoundError:
            return
        if size < self.offset:  # reopened by another Editor
            self.offset, self.partial, self.restarts = 0, b'', self.restarts+1
            self.shutdown = None
        if size == self.offset:
            return
        with self.path.open('rb') as handle:
            handle.seek(self.offset)
            data = handle.read(size-self.offset)
        self.offset += len(data)
        lines = (self.partial+data).split(b'\n')
        self.partial = lines.pop()
        for line in lines:
            if self.renderer is None and line.startswith(b'Renderer:'):
                self.renderer = line[len(b'Renderer:'):].strip().decode(errors='replace')
            if self.shutdown is None and any(m in line for m in SHUTDOWN_MARKERS):
                self.shutdown = now
            if self.hub is None and HUB_MARKER in line:
                self.hub = now


def _kill_on_parent_death():
    libc = ctypes.CDLL(None, use_errno=True)
    libc.prctl(1, signal.SIGKILL)  # PR_SET_PDEATHSIG


def find_relaunch(project, exclude, table=None):
    keys = path_keys(project)
    table = list(processes()) if table is None else table
    return next((p for p in table if is_editor(p) and keys & set(p.argv) and p.pid not in exclude), None)


def owner_alive(spec):
    if not spec.get('owner_pid'):
        return True
    owner = read_proc(spec['owner_pid'])
    return owner is not None and owner.start_ticks == spec['owner_start_ticks']


def progress_signature(paths):
    result = []
    for path in paths:
        try:
            stat = Path(path).stat()
            result.append((stat.st_mtime_ns, stat.st_size))
        except FileNotFoundError:
            result.append(None)
    return result


def supervise(spec, poll=.25):
    # Create a display only after admission, inside this job's lease.
    display = Display(spec['screen']) if spec.get('private_display') and owner_alive(spec) else None
    try:
        return _supervise(dict(spec, display=display.name) if display else spec, poll)
    finally:
        if display:
            display.close()


def _supervise(spec, poll=.25):
    """Runs one Editor described by `spec` (a dict, see EditorJob) and returns its result."""
    out = Path(spec['out'])
    out.mkdir(parents=True, exist_ok=True)
    started_wall = time.time()
    start = time.monotonic()
    result = {'format': 1, 'label': spec.get('label'), 'kind': spec.get('kind'),
              'command': spec['command'], 'project': spec['project'], 'log': spec['log'],
              'display': spec.get('display'), 'require_gpu': spec.get('require_gpu', False),
              'exit_code': None, 'signal': None, 'forced_exit': None, 'forced_detail': None,
              'renderer': None, 'renderer_ok': None, 'shutdown_logged': False,
              'hub_launched': False, 'followed': [], 'stopped': [], 'leftovers': [],
              'swept': [], 'times': {'admitted': round(started_wall, 3)}}
    watch = LogWatch(spec['log'])
    stopping = []
    if not owner_alive(spec):
        result.update(forced_exit='owner-exit', forced_detail='the initiating driver already exited')
        write_json(out/RESULT, result)
        return result
    progress = progress_signature(spec.get('progress_files', []))
    last_progress = start

    def cancel(number, frame):
        stopping.append(signal.Signals(number).name)
    previous = {s: signal.signal(s, cancel) for s in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP)}
    with (out/'editor.stdout.log').open('ab') as stdout:
        editor = subprocess.Popen(spec['command'], cwd=spec.get('cwd'), stdin=subprocess.DEVNULL,
                                  stdout=stdout, stderr=subprocess.STDOUT,
                                  env=dict(os.environ, DISPLAY=spec['display']) if spec.get('display') else None,
                                  preexec_fn=_kill_on_parent_death)
    current = Proc(editor.pid, os.getpid(), editor.pid, 'R', [])
    seen = {editor.pid}

    def stop(reason, detail):
        if result['forced_exit'] is None:
            result['forced_exit'], result['forced_detail'] = reason, detail
            result['times']['forced_s'] = round(time.monotonic()-start, 2)
        result['stopped'] += terminate(family(current.pid), spec.get('kill_grace', 10), reap=editor)

    def running():
        if current.pid == editor.pid:
            return editor.poll() is None
        return read_proc(current.pid) is not None

    while True:
        now = time.monotonic()
        watch.poll(now)
        current_progress = progress_signature(spec.get('progress_files', []))
        if current_progress != progress:
            progress, last_progress = current_progress, now
            result['times']['last_progress_s'] = round(now-start, 2)
        if watch.renderer is not None and result['renderer'] is None:
            result['renderer'] = watch.renderer
            result['times']['renderer_s'] = round(now-start, 2)
        if watch.hub is not None and not result['hub_launched']:
            result['hub_launched'] = True
            result['times']['hub_s'] = round(watch.hub-start, 2)
        if watch.shutdown is not None and 'shutdown_s' not in result['times']:
            result['shutdown_logged'] = True
            result['times']['shutdown_s'] = round(watch.shutdown-start, 2)
        alive_now = running()
        if alive_now:
            if stopping:
                stop('cancelled', 'the supervisor received '+stopping[0])
            elif not owner_alive(spec):
                stop('owner-exit', 'the initiating driver exited')
            elif spec.get('require_gpu') and watch.renderer is not None and 'NVIDIA' not in watch.renderer:
                stop('renderer', 'the Editor renders with '+watch.renderer)
            elif watch.shutdown is not None and now-watch.shutdown > spec['shutdown_grace']:
                stop('shutdown-guard', f"still alive {spec['shutdown_grace']} s after the shutdown line")
            elif now-start > spec['timeout']:
                stop('watchdog', f"still running after {spec['timeout']} s")
            elif spec.get('progress_timeout') and now-last_progress > spec['progress_timeout']:
                stop('no-progress', f"no probe output progress for {spec['progress_timeout']} s")
            else:
                time.sleep(poll)
                continue
            alive_now = running()
            if alive_now:  # KILL was sent; the kernel may need a moment
                time.sleep(poll)
                continue
        if current.pid == editor.pid:
            editor.wait()
            code = editor.returncode
            result['exit_code'] = code if code >= 0 else 128-code
            result['signal'] = signal.Signals(-code).name if code < 0 else None
            result['times']['exit_s'] = round(time.monotonic()-start, 2)
        else:
            result['followed'][-1]['exit_s'] = round(time.monotonic()-start, 2)
        # A windowed Editor that leaves before its shutdown line may have handed the
        # project to another Editor process (through the Hub). Follow it.
        if result['forced_exit'] is None and watch.shutdown is None and not stopping:
            relaunch = None
            deadline = time.monotonic()+spec.get('follow', 30)
            while relaunch is None and time.monotonic() < deadline and not stopping:
                relaunch = find_relaunch(spec['project'], seen)
                if relaunch is None:
                    time.sleep(.5)
            if relaunch is not None:
                seen.add(relaunch.pid)
                current = relaunch
                result['followed'].append({'pid': relaunch.pid, 'cmd': relaunch.describe(),
                                           'found_s': round(time.monotonic()-start, 2)})
                continue
        break
    # Members of the Editor's group that outlived it (the licensing client excluded).
    result['leftovers'] = terminate(family(editor.pid)+[p for f in result['followed']
                                                        for p in family(f['pid'])],
                                    spec.get('kill_grace', 10))
    watch.poll(time.monotonic())
    result['shutdown_logged'] = watch.shutdown is not None or result['shutdown_logged']
    result['renderer'] = result['renderer'] or watch.renderer
    result['renderer_ok'] = None if result['renderer'] is None else 'NVIDIA' in result['renderer']
    result['swept'] = sweep(spec['project'])
    result['times']['finished_s'] = round(time.monotonic()-start, 2)
    for s, handler in previous.items():
        signal.signal(s, handler)
    write_json(out/RESULT, result)
    return result


def write_json(path, value):
    temp = Path(str(path)+'.tmp')
    temp.write_text(json.dumps(value, indent=1)+'\n')
    temp.replace(path)


# ---------------------------------------------------------------- runner side

class Display:
    """A private Xvfb; the server picks a free display number itself."""

    def __init__(self, screen):
        read, write = os.pipe()
        self.server = subprocess.Popen(['Xvfb', '-displayfd', str(write), '-screen', '0', screen,
                                        '-nolisten', 'tcp'], pass_fds=(write,),
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        os.close(write)
        number = b''
        with os.fdopen(read, 'rb') as handle:
            while not number.endswith(b'\n'):
                chunk = handle.read(1)
                if not chunk:
                    self.close()
                    raise RuntimeError('Xvfb did not start')
                number += chunk
        self.name = ':'+number.decode().strip()

    def close(self):
        if self.server.poll() is None:
            self.server.terminate()
            try:
                self.server.wait(10)
            except subprocess.TimeoutExpired:
                self.server.kill()
                self.server.wait()


def run(job):
    """Runs `job` through the heavy-job launcher and returns the supervisor's result."""
    job.out.mkdir(parents=True, exist_ok=True)
    if (job.out/RESULT).exists():
        raise SystemExit(f'refusing to overwrite an Editor result: {job.out/RESULT}')
    queued = time.time()
    env = dict(os.environ, PWD=str(job.project), **job.env)
    env.pop('XAUTHORITY', None)
    owner = read_proc(os.getpid())
    spec = {'label': job.label, 'kind': job.kind, 'command': job.command(), 'project': str(job.project),
            'log': str(job.log), 'out': str(job.out), 'cwd': str(job.cwd), 'timeout': job.timeout,
            'shutdown_grace': job.shutdown_grace, 'kill_grace': job.kill_grace, 'follow': job.follow,
            'require_gpu': job.graphics, 'private_display': job.graphics, 'screen': job.screen,
            'owner_pid': owner.pid, 'owner_start_ticks': owner.start_ticks,
            'progress_files': job.progress_files, 'progress_timeout': job.progress_timeout}
    spec_path = job.out/'editor-spec.json'
    write_json(spec_path, spec)
    scope = uuid.uuid4().hex
    launcher = [str(LAUNCHER), '--kind', job.kind, '--scope-id', scope,
                sys.executable, str(Path(__file__).resolve()), 'supervise', str(spec_path)]
    process = subprocess.Popen(launcher, cwd=job.cwd, env=env)
    try:
        code = process.wait()
    except BaseException:
        process.terminate()
        process.wait()
        raise
    finished = time.time()
    path = job.out/RESULT
    if path.exists():
        result = json.loads(path.read_text())
    else:
        result = {'format': 1, 'label': job.label, 'kind': job.kind, 'command': spec['command'],
                  'project': str(job.project), 'log': str(job.log), 'exit_code': None,
                  'forced_exit': 'supervisor', 'forced_detail': f'the launcher exited with {code} '
                  'without an Editor result', 'renderer': None, 'renderer_ok': None,
                  'shutdown_logged': False, 'times': {}}
    result['launcher'] = {'command': launcher, 'exit': code, 'scope': 'unbound-editor-'+scope+'.scope'}
    times = result.setdefault('times', {})
    times['queued'] = round(queued, 3)
    if 'admitted' in times:
        times['lease_wait_s'] = round(times['admitted']-queued, 2)
    times['total_s'] = round(finished-queued, 2)
    result['swept_after'] = sweep(job.project)
    result['late_sweeps'] = schedule_sweeps(job.project, job.late_sweeps, job.out/'hub-sweeps.jsonl')
    write_json(path, result)
    return result


def run_parallel(jobs):
    """Runs independent jobs at the same time (each waits for its own lease)."""
    results = [None]*len(jobs)
    errors = []

    def one(index):
        try:
            results[index] = run(jobs[index])
        except BaseException as error:  # reported after all threads end
            errors.append(error)
    threads = [threading.Thread(target=one, args=(i,)) for i in range(len(jobs))]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    if errors:
        raise errors[0]
    return results


def summary(result):
    times = result.get('times', {})
    forced = f", forced: {result['forced_exit']}" if result.get('forced_exit') else ''
    return (f"{result.get('label')}: exit {result.get('exit_code')}{forced}, renderer "
            f"{result.get('renderer')!r}, Editor {times.get('exit_s')} s, lease wait "
            f"{times.get('lease_wait_s')} s, total {times.get('total_s')} s")


# ---------------------------------------------------------------- publishing gate

def load_result(directory):
    """The Editor result of a capture directory. Captures from runners that predate this
    helper keep exit.txt/forced-exit.txt/watchdog.txt next to editor.log."""
    directory = Path(directory)
    path = directory/RESULT
    if path.exists():
        return json.loads(path.read_text())
    if not (directory/'exit.txt').exists():
        return None
    log = (directory/'editor.log').read_text(errors='replace') if (directory/'editor.log').exists() else ''
    renderer = next((line.split(':', 1)[1].strip() for line in log.splitlines() if line.startswith('Renderer:')), None)
    forced = (directory/'forced-exit.txt').exists()
    return {'exit_code': int((directory/'exit.txt').read_text().strip() or -1),
            'forced_exit': 'shutdown-guard' if forced else None, 'renderer': renderer,
            'shutdown_logged': forced or 'Cleanup mono' in log, 'require_gpu': True}


def result_problem(directory, gpu=None):
    """Why this capture's Editor run cannot back a reference (None if it can)."""
    result = load_result(directory)
    if result is None:
        return 'no Editor result'
    forced = result.get('forced_exit')
    if forced and not (forced in FORCED_PUBLISHABLE and result.get('shutdown_logged')):
        return f"the Editor was stopped ({forced}: {result.get('forced_detail')})"
    if not forced and result.get('exit_code') != 0:
        return 'the Editor did not exit with 0'
    if (result.get('require_gpu') if gpu is None else gpu) and 'NVIDIA' not in (result.get('renderer') or ''):
        return 'NVIDIA GPU renderer was not verified'
    return None


def jsonl_complete(name='values.jsonl', marker=None):
    """A completion check: `name` holds JSON lines whose last line, and only that one, is
    `marker` (default {"k": "complete", "v": true})."""
    marker = {'k': 'complete', 'v': True} if marker is None else marker

    def check(directory):
        path = Path(directory)/name
        if not path.exists():
            return f'{name} is missing'
        try:
            rows = [json.loads(line) for line in path.read_text().splitlines() if line.strip()]
        except json.JSONDecodeError:
            return f'{name} is not complete JSON lines'
        if not rows or rows[-1] != marker or rows.count(marker) != 1:
            return f'{name} does not end with its completion marker {json.dumps(marker)}'
        return None
    return check


def differences(directories, names):
    """Problems where repeats differ: `names(directory)` lists the compared files."""
    first = Path(directories[0])
    expected = sorted(names(first))
    problems = []
    for other in directories[1:]:
        other = Path(other)
        listed = sorted(names(other))
        if listed != expected:
            problems.append(f'{other.name} has other files than {first.name}: '
                            f'{sorted(set(listed) ^ set(expected))[:5]}')
            continue
        changed = [n for n in expected if (first/n).read_bytes() != (other/n).read_bytes()]
        if changed:
            problems.append(f'{other.name} differs from {first.name} in {changed[:5]}')
    return problems


def publish(directories, target, names, write, complete=(), gpu=None):
    """Checks every repeat (Editor result, completion checks), that all repeats are
    byte-identical in `names`, and that `target` is new; then calls write(target, first)."""
    directories = [Path(d) for d in directories]
    if len(directories) < 2:
        sys.exit('publishing needs two independent repeats')
    for directory in directories:
        reason = result_problem(directory, gpu)
        for check in ([] if reason else list(complete)):
            reason = check(directory)
            if reason:
                break
        if reason:
            sys.exit(f'{directory.name}: {reason}; not publishing')
    problems = differences(directories, names)
    if problems:
        sys.exit('the repeats differ ('+'; '.join(problems)+'); not publishing')
    target = Path(target)
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    write(target, directories[0])
    return 0


# ---------------------------------------------------------------- command line

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    sup = sub.add_parser('supervise', help='internal: run one Editor inside its lease')
    sup.add_argument('spec', type=Path)
    sw = sub.add_parser('sweep', help="end the Hub/Roslyn processes a project copy left behind")
    sw.add_argument('--project', type=Path, required=True)
    sw.add_argument('--delays', default='0', help='comma-separated seconds after start')
    sw.add_argument('--log', type=Path)
    args = parser.parse_args(argv)
    if args.command == 'supervise':
        result = supervise(json.loads(args.spec.read_text()))
        code = result['exit_code']
        return code if code is not None else 125
    begin = time.monotonic()
    for delay in [float(d) for d in args.delays.split(',') if d.strip()]:
        time.sleep(max(0, begin+delay-time.monotonic()))
        swept = sweep(args.project)
        entry = {'after_s': delay, 'time': round(time.time(), 1), 'swept': swept}
        if args.log:
            with args.log.open('a') as handle:
                handle.write(json.dumps(entry)+'\n')
        else:
            print(json.dumps(entry))
    return 0


if __name__ == '__main__':
    sys.exit(main())
