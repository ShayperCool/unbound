#!/usr/bin/env python3
"""Shared release selection and process guard for engine-only probe runs."""
import os
from pathlib import Path
import re
import select
import shlex
import signal
import subprocess
import sys
import threading
import time

NO_EDITOR = 'UNBOUND_SUITE_NO_EDITOR'
EDITOR_EXIT = 126
EDITOR_POLL = 1.0


def require_process_watch():
    """Refuse guarded execution when the process-tree backend is unavailable."""
    if (sys.platform != 'linux' or not Path('/proc/self').is_dir()
            or not hasattr(os, 'pidfd_open') or not hasattr(signal, 'pidfd_send_signal')):
        raise RuntimeError('Editor launch guard requires Linux /proc and pidfd support; '
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


def children():
    """{parent pid: [child pids]} of every process."""
    parents = {}
    for stat in Path('/proc').glob('[0-9]*/stat'):
        try:
            fields = stat.read_text().rsplit(')', 1)[1].split()
        except (OSError, IndexError):
            continue
        parents.setdefault(int(fields[1]), []).append(int(stat.parent.name))
    return parents


class Tree:
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
    """Portable cleanup for a private session, including descendants in other groups."""
    tracked, groups = {process.pid}, {process.pid}

    def collect():
        rows = subprocess.check_output(['ps', '-A', '-o', 'pid=', '-o', 'ppid=',
                                        '-o', 'pgid=', '-o', 'stat='], text=True)
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
        for pid, parent, group, state in records:
            if int(pid) in tracked:
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
    if (sys.platform != 'linux' or not Path('/proc/self').is_dir()
            or not callable(getattr(os, 'pidfd_open', None))
            or not callable(getattr(signal, 'pidfd_send_signal', None))):
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
    try:
        argv = (Path('/proc')/str(pid)/'cmdline').read_bytes().split(b'\0')
    except OSError:
        return []
    return [arg.decode(errors='replace') for arg in argv if arg]


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
    try:
        executable = os.readlink(f'/proc/{pid}/exe')
    except OSError:
        executable = ''
    if (argv and is_editor_path(argv[0])) or is_editor_path(executable.removesuffix(' (deleted)')):
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
            parents = children() if trees else {}
            for pid, tree in trees:
                tree.collect(parents)
                tree.prune()
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
