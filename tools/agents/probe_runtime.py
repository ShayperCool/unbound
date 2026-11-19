#!/usr/bin/env python3
"""Shared release selection and process guard for engine-only probe runs."""
import hashlib
import os
from pathlib import Path
import re
import select
import shlex
import shutil
import signal
import subprocess
import sys
import threading
import time

NO_EDITOR = 'UNBOUND_SUITE_NO_EDITOR'
EDITOR_EXIT = 126
EDITOR_POLL = 1.0


IMAGE_MODULES = 'import PIL, numpy'
_IMAGE_READY = {}


def has_image_modules(interpreter):
    """Whether interpreter can import PIL and numpy, which image comparators need."""
    if interpreter not in _IMAGE_READY:
        try:
            _IMAGE_READY[interpreter] = subprocess.run(
                [interpreter, '-c', IMAGE_MODULES], capture_output=True, timeout=60).returncode == 0
        except (OSError, subprocess.TimeoutExpired):
            _IMAGE_READY[interpreter] = False
    return _IMAGE_READY[interpreter]


def python_with_pil():
    """An interpreter that can import PIL and numpy (image comparators need them), else this one."""
    for candidate in (sys.executable, '/usr/bin/python3', shutil.which('python3')):
        if candidate and has_image_modules(candidate):
            return candidate
    return sys.executable


def probe_path(environment, shims):
    """PATH for probe shell commands, whose `python3` can import PIL and numpy.

    Probe commands name `python3`, and image comparators need PIL and numpy. When the first
    python3 on PATH lacks them but python_with_pil() finds an interpreter that has them, a
    private directory under `shims` whose python3 links to that interpreter goes first.
    Otherwise PATH is unchanged and a missing module fails in the comparator itself."""
    path = environment.get('PATH', os.defpath)
    current = shutil.which('python3', path=path)
    if current and has_image_modules(current):
        return path
    chosen = python_with_pil()
    if not has_image_modules(chosen):
        return path
    directory = Path(shims)/hashlib.sha256(os.fsencode(chosen)).hexdigest()[:16]
    link = directory/'python3'
    if not (link.is_symlink() and os.readlink(link) == chosen):
        directory.mkdir(parents=True, exist_ok=True)
        staged = directory/f'.python3-{os.getpid()}-{time.time_ns()}'
        os.symlink(chosen, staged)
        os.replace(staged, link)
    return str(directory)+os.pathsep+path


def pidfd_backend():
    """Linux: processes are read from /proc and held by pidfd."""
    return (sys.platform == 'linux' and Path('/proc/self').is_dir()
            and callable(getattr(os, 'pidfd_open', None)) and callable(getattr(signal, 'pidfd_send_signal', None)))


def ps_backend():
    """macOS: processes are read from ps snapshots and told apart by their start times."""
    return sys.platform == 'darwin' and shutil.which('ps') is not None


def require_process_watch():
    """Refuse guarded execution when no process-tree backend is available."""
    if ps_backend():
        return
    if not pidfd_backend():
        raise RuntimeError('Editor launch guard requires Linux /proc and pidfd support or macOS ps; '
                           f'guarded probe execution is unsupported on {sys.platform}')
    try:
        fd = os.pidfd_open(os.getpid())
        os.close(fd)
    except OSError as error:
        raise RuntimeError(f'Editor launch guard cannot open a process handle: {error}') from error


RELEASE_VARIABLE = 'UNBOUND_PROBE_UNITY_VERSION'
PROBE_DIR = re.compile(r'tests/projects/([A-Za-z0-9_.-]+)')


def default_release():
    """tests/probe_paths.py's DEFAULT_UNITY_VERSION, the release the probes' references are named by."""
    text = (Path(__file__).resolve().parents[2]/'tests/probe_paths.py').read_text()
    return re.search(r"^DEFAULT_UNITY_VERSION = '([^']+)'", text, re.M).group(1)


def probe_release(command, root, environment=None):
    """The release a --probe command runs for: $UNBOUND_PROBE_UNITY_VERSION when exported, else the
    one release of the probe projects it names, else the default. An optional environment
    preserves the individual caller's override when a train leader selects checks."""
    environment = os.environ if environment is None else environment
    if environment.get(RELEASE_VARIABLE):
        return environment[RELEASE_VARIABLE]
    releases = set()
    for name in PROBE_DIR.findall(command):
        version = root/'tests/projects'/name/'ProjectSettings/ProjectVersion.txt'
        if version.is_file():
            match = re.match(r'm_EditorVersion: (\S+)', version.read_text())
            if match:
                releases.add(match.group(1))
    if len(releases) > 1:
        raise ValueError(f'--probe {command!r} names probes of releases {", ".join(sorted(releases))}: '
                         f'export {RELEASE_VARIABLE}')
    return releases.pop() if releases else default_release()


def checkout_state(root, paths=('.',)):
    """The checkout's uncommitted and untracked (not ignored) files under `paths` (relative to
    `root`): {path: (git status, content digest)}. Probe runs work on copies of their projects
    (tests/probe_paths.py project_copy), as Unbound and the Editor write a project's files;
    checkout_writes compares two states to find a run that wrote into the checkout anyway. A
    directory outside any Git checkout (a test's scratch tree) has no state: {}."""
    if not any((directory/'.git').exists() for directory in (Path(root).resolve(), *Path(root).resolve().parents)):
        return {}
    listed = subprocess.run(['git', '-c', 'status.renames=false', '--no-optional-locks', 'status',
                             '--porcelain=v1', '-z', '--untracked-files=all', '--ignore-submodules=all',
                             '--', *paths], cwd=root, capture_output=True, check=True).stdout
    state = {}
    for entry in filter(None, listed.split(b'\0')):
        path = os.fsdecode(entry[3:])
        file = Path(root)/path
        digest = (hashlib.sha256(file.read_bytes()).hexdigest()
                  if file.is_file() and not file.is_symlink() else None)
        state[path] = (entry[:2].decode(), digest)
    return state


def checkout_writes(root, before, paths=('.',)):
    """The files under `paths` a run created, changed or removed since checkout_state `before`:
    sorted 'XY path' lines in git's porcelain form (' D' for a file the run restored)."""
    after = checkout_state(root, paths)
    inside = lambda path: any(p in ('.', '') or path == p or path.startswith(p.rstrip('/')+'/') for p in paths)
    changed = [f'{after[path][0]} {path}' for path in sorted(after) if before.get(path) != after[path]]
    restored = [f'{before[path][0]} {path} (restored)' for path in sorted(before)
                if path not in after and inside(path)]
    return changed + restored


# ps's dates and column words follow the locale and the time zone: fix both, so that a start
# time is always five words and stays the same while a run's zone changes.
PS_ENVIRONMENT = {'LC_ALL': 'C', 'TZ': 'UTC0'}


def ps(*columns):
    """The output of `ps -A` with these arguments (OSError when ps fails). Popen directly: a
    caller that replaces subprocess.run (a test's mock) must not leave the process guard without
    a listing."""
    with subprocess.Popen(['ps', '-A', *columns], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True,
                          env=dict(os.environ, **PS_ENVIRONMENT)) as listing:
        out, _ = listing.communicate()
    if listing.returncode:
        raise OSError(f'ps exited {listing.returncode}')
    return out


class PsRow:
    """A process in a ps snapshot: its parent, state, start time, executable and arguments."""
    __slots__ = ('parent', 'state', 'start', 'executable', 'argv')

    def __init__(self, parent, state, start, executable):
        self.parent, self.state, self.start, self.executable, self.argv = parent, state, start, executable, []


# The latest ps snapshot ({pid: PsRow}): children() takes one, cmdline() and editor_process()
# read it for the processes of the same scan.
PS_SNAPSHOT = {}
PS_LOCK = threading.Lock()


def ps_snapshot():
    """{pid: PsRow} of every process, from two ps listings (macOS ps prints the executable's path
    and the arguments in separate columns; lstart is five words in the C locale). OSError when ps
    fails or prints a row it cannot read."""
    rows = {}
    listing = ps('-o', 'pid=', '-o', 'ppid=', '-o', 'stat=', '-o', 'lstart=', '-o', 'comm=')
    for line in listing.splitlines():
        fields = line.split(None, 8)
        if not fields:
            continue
        if len(fields) != 9 or not (fields[0].isdigit() and fields[1].isdigit()):
            raise OSError(f'unreadable ps row: {line!r}')
        rows[int(fields[0])] = PsRow(int(fields[1]), fields[2], ' '.join(fields[3:8]), fields[8])
    arguments = ps('-ww', '-o', 'pid=', '-o', 'args=')
    for line in arguments.splitlines():
        fields = line.split()
        if fields and int(fields[0]) in rows:
            rows[int(fields[0])].argv = fields[1:]
    with PS_LOCK:
        PS_SNAPSHOT.clear()
        PS_SNAPSHOT.update(rows)
    return rows


def children():
    """{parent pid: [child pids]} of every process."""
    if not pidfd_backend():
        parents = {}
        for pid, row in ps_snapshot().items():
            parents.setdefault(row.parent, []).append(pid)
        return parents
    parents = {}
    for stat in Path('/proc').glob('[0-9]*/stat'):
        try:
            fields = stat.read_text().rsplit(')', 1)[1].split()
        except (OSError, IndexError):
            continue
        parents.setdefault(int(fields[1]), []).append(int(stat.parent.name))
    return parents


def Tree(root):
    """The process tree of a step: held by pidfd on Linux, by pid and start time on macOS."""
    return PidfdTree(root) if pidfd_backend() else PsTree(root)


class PsTree:
    """A step's process group and every descendant seen in ps snapshots (macOS). A process is
    known by its pid and start time, so that a pid used again by another process is never
    signalled; one reparented after its parent died stays tracked once it has been seen. The
    tracked processes are the keys of `fds` (as with PidfdTree), the start times its values."""

    def __init__(self, root):
        self.root, self.fds = root, {}

    def collect(self, parents=None):
        parents = children() if parents is None else parents
        with PS_LOCK:
            rows = dict(PS_SNAPSHOT)
        pending, seen = [self.root, *self.fds], set()
        while pending:
            pid = pending.pop()
            if pid in seen:
                continue
            seen.add(pid)
            row = rows.get(pid)
            if pid not in self.fds:
                if row is None or licensing(pid):
                    continue  # gone, or the shared Unity licensing client, never signalled
                self.fds[pid] = row.start
            elif row is None or row.start != self.fds[pid]:
                continue
            pending += parents.get(pid, [])

    def live(self, rows=None):
        """The tracked processes that still run: the same pid and start time, not a zombie (in a
        new snapshot unless rows are given)."""
        rows = ps_snapshot() if rows is None else rows
        return [pid for pid, start in self.fds.items()
                if pid in rows and rows[pid].start == start and not rows[pid].state.startswith('Z')]

    def prune(self):
        """Forget the processes that have exited (in the snapshot the last collect() took)."""
        with PS_LOCK:
            rows = dict(PS_SNAPSHOT)
        running = set(self.live(rows))
        for pid in list(self.fds):
            if pid != self.root and pid not in running:
                del self.fds[pid]

    def signal(self, number):
        """The group and every tracked process still running. Without a listing (ps failed)
        only the group: a pid is never signalled unless its start time was confirmed."""
        try:
            self.collect()
            running = self.live()
        except OSError as error:
            print(f'process guard: {error}; signalling process group {self.root} only', file=sys.stderr)
            running = []
        try:
            os.killpg(self.root, number)
        except OSError:
            pass
        for pid in running:
            try:
                os.kill(pid, number)
            except OSError:
                pass

    def alive(self):
        """Whether a tracked process still runs; without a listing it is taken to run."""
        try:
            return bool(self.live())
        except OSError:
            return True

    def close(self):
        self.fds = {}


class PidfdTree:
    """A step's process group and every descendant, held by pidfd so that a process that leaves
    the group (`timeout` makes its own) or is reparented when its parent dies is still reached."""

    def __init__(self, root):
        self.root, self.fds = root, {}

    def collect(self, parents=None):
        parents = children() if parents is None else parents
        pending, seen = [self.root, *self.fds], set()
        while pending:
            pid = pending.pop()
            if pid in seen:
                continue
            seen.add(pid)
            if pid not in self.fds:
                if licensing(pid):
                    continue  # the shared Unity licensing client is never signalled
                try:
                    self.fds[pid] = os.pidfd_open(pid)
                except OSError:
                    continue
            pending += parents.get(pid, [])

    def prune(self):
        """Forget the processes that have exited."""
        for pid, fd in list(self.fds.items()):
            if pid != self.root:
                try:
                    signal.pidfd_send_signal(fd, 0)
                except OSError:
                    os.close(fd)
                    del self.fds[pid]

    def signal(self, number):
        self.collect()
        try:
            os.killpg(self.root, number)
        except OSError:
            pass
        for fd in self.fds.values():
            try:
                signal.pidfd_send_signal(fd, number)
            except OSError:
                pass

    def alive(self):
        # A readable pidfd denotes an exited process, including an unreaped zombie.
        poll = select.poll()
        for fd in self.fds.values():
            poll.register(fd, select.POLLIN)
        exited = {fd for fd, _ in poll.poll(0)}
        return any(fd not in exited for fd in self.fds.values())

    def close(self):
        for fd in self.fds.values():
            os.close(fd)
        self.fds = {}


def stop_groups(process, grace):
    """Portable cleanup of a process and its descendants: their process groups are signalled,
    and the tracked processes in the caller's own group (a process started without a session of
    its own) one by one, so that the caller is never signalled. Pids are not checked against
    start times here (PsTree does that for the Editor guard): within the grace period a pid is
    used again only after the system's pid counter wraps around."""
    tracked, groups, own = {process.pid}, {process.pid}, set()

    def collect():
        """Whether a tracked process still runs; without a listing (ps failed) the groups and
        processes already known stay signalled and the tree is taken to run."""
        try:
            rows = ps('-o', 'pid=', '-o', 'ppid=', '-o', 'pgid=', '-o', 'stat=')
        except OSError as error:
            print(f'process guard: {error}; signalling the known groups only', file=sys.stderr)
            return True
        records = [line.split() for line in rows.splitlines() if line.strip()]
        parents = {}
        for pid, parent, group, state in records:
            parents.setdefault(int(parent), []).append(int(pid))
        pending = list(tracked)
        seen = set()
        while pending:
            pid = pending.pop()
            if pid in seen:
                continue
            seen.add(pid)
            pending += parents.get(pid, [])
        tracked.update(seen)
        live = False
        own.clear()
        for pid, parent, group, state in records:
            if int(pid) in tracked:
                if int(group) == os.getpgrp():
                    if int(pid) != os.getpid() and not state.startswith('Z'):
                        own.add(int(pid))
                else:
                    groups.add(int(group))
                live |= not state.startswith('Z')
        return live

    def signal_groups(number):
        for group in groups:
            if group == os.getpgrp():
                continue
            try:
                os.killpg(group, number)
            except ProcessLookupError:
                pass
        for pid in own:
            try:
                os.kill(pid, number)
            except ProcessLookupError:
                pass
        if process.poll() is None:
            # The step's own process, even before its group is known: Popen holds its pid.
            process.send_signal(number)

    collect()
    signal_groups(signal.SIGTERM)
    deadline = time.monotonic()+grace
    while time.monotonic() < deadline:
        live = collect()
        if process.poll() is not None and not live:
            break
        signal_groups(signal.SIGTERM)
        time.sleep(0.05)
    signal_groups(signal.SIGKILL)
    process.wait()


def stop_tree(process, grace=30):
    """SIGTERM to the step's whole tree; SIGKILL to whatever is left after the grace period."""
    if not pidfd_backend():
        return stop_groups(process, grace)
    tree = Tree(process.pid)
    try:
        tree.signal(signal.SIGTERM)
        deadline = time.time() + grace
        while time.time() < deadline:
            if process.poll() is not None and not tree.alive():
                break
            time.sleep(0.5)
        tree.signal(signal.SIGKILL)
        process.wait()
    finally:
        tree.close()


def cmdline(pid):
    """A process's arguments (on macOS from the latest ps snapshot, split at white space)."""
    if not pidfd_backend():
        with PS_LOCK:
            row = PS_SNAPSHOT.get(pid)
        return list(row.argv) if row else []
    try:
        argv = (Path('/proc')/str(pid)/'cmdline').read_bytes().split(b'\0')
    except OSError:
        return []
    return [arg.decode(errors='replace') for arg in argv if arg]


def executable_path(pid):
    """The path of a process's executable ('' when unknown)."""
    if not pidfd_backend():
        with PS_LOCK:
            row = PS_SNAPSHOT.get(pid)
        return row.executable if row else ''
    try:
        return os.readlink(f'/proc/{pid}/exe').removesuffix(' (deleted)')
    except OSError:
        return ''


def licensing(pid):
    joined = ' '.join(cmdline(pid))
    return 'Licensing' in joined or 'LicenseClient' in joined


def is_editor_path(arg):
    """The Unity Editor executable: <release>/Editor/Unity, or Unity.app/Contents/MacOS/Unity."""
    path = Path(arg)
    return path.name == 'Unity' and (path.parent.name == 'Editor' or path.parent.parent.parent.name == 'Unity.app')


def editor_process(pid):
    """What makes process pid an Editor launch: the Editor itself, or a heavy-job launcher holding
    or queueing an Editor job (an editor kind, or the Editor as its command). None otherwise."""
    argv = cmdline(pid)
    executable = executable_path(pid)
    if (argv and is_editor_path(argv[0])) or is_editor_path(executable):
        return shlex.join(argv)[:300] or executable
    launcher = any(Path(arg).name in ('with-heavy-job', 'job_runner.py') for arg in argv)
    kinds = [argv[i+1] for i, arg in enumerate(argv[:-1]) if arg in ('--kind', '--pool-worker')]
    if launcher and (any(kind.startswith('editor-') for kind in kinds) or any(map(is_editor_path, argv))):
        return shlex.join(argv)[:300]
    return None


EDITOR_MARK = 'step launched the Unity Editor'


class EditorWatch:
    """Polls the process tree of every running step and stops a step that starts the Unity Editor.

    A suite (or gate) run compares the engine with the committed Reference/; Editor runs only record
    references. The step's process is told by remove(), which returns what was found."""

    def __init__(self, grace=30):
        self.lock, self.trees, self.found, self.thread = threading.Lock(), {}, {}, None
        self.stopping, self.grace = {}, grace

    def add(self, pid):
        require_process_watch()
        with self.lock:
            self.trees[pid] = Tree(pid)
            if self.thread is None:
                self.thread = threading.Thread(target=self.loop, daemon=True)
                self.thread.start()

    def remove(self, pid):
        with self.lock:
            tree = self.trees.pop(pid, None)
            found = self.found.pop(pid, None)
            stopping = self.stopping.pop(pid, None)
        if stopping is not None:
            stopping.join()
        if tree is not None:
            tree.close()
        return found

    def loop(self):
        while True:
            time.sleep(EDITOR_POLL)
            self.scan()

    def scan(self):
        with self.lock:
            trees = [(pid, tree) for pid, tree in self.trees.items() if pid not in self.found]
            try:
                parents = children() if trees else {}
                for _, tree in trees:
                    tree.collect(parents)
                    tree.prune()
            except OSError as error:
                # A listing failed (macOS ps): the watch keeps running and scans again next poll.
                print(f'process guard: {error}; scanning again', file=sys.stderr)
                return
            for pid, tree in trees:
                found = next(filter(None, map(editor_process, tree.fds)), None)
                if found:
                    self.found[pid] = found
                    # Keep the captured descendants even if the shell exits before cleanup
                    # finishes. remove() waits before closing these process handles.
                    stopping = threading.Thread(target=stop_root, args=(tree, self.grace))
                    self.stopping[pid] = stopping
                    stopping.start()


def stop_root(tree, grace=30):
    """SIGTERM to the tracked tree, then SIGKILL before its owner may return."""
    tree.signal(signal.SIGTERM)
    deadline = time.monotonic() + grace
    while time.monotonic() < deadline and tree.alive():
        time.sleep(min(0.5, max(0, deadline - time.monotonic())))
    tree.signal(signal.SIGKILL)


EDITOR_WATCH = EditorWatch()
