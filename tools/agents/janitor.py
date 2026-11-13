#!/usr/bin/env python3
"""Conservative disk hygiene for run caches, completed tasks and orphan targets.

Default is dry-run. Use --apply after inspecting it. JSON lines go to stdout and
agents/janitor/<timestamp>.jsonl. No worktree or live checkout target is removed.
"""
import argparse
import ctypes
import errno
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import shlex
import sqlite3
import stat
import struct
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build
from slot import main_checkout, slot_root, worktrees
from run_cache_index import DirectoryIndex
import runtime_snapshot

GIB = 1024**3
# Three days of captures, and a separate last-write grace for every deletion.
DEFAULT_RUN_DAYS = 3
DEFAULT_IDLE_HOURS = 24
DEFAULT_MIN_FREE_GIB = 20
DEFAULT_SCAN_SECONDS = 1200
DEFAULT_RUN_SECONDS = 1800
MAX_REDISCOVERIES = 2
# Keep land's lock independent of a service manager or a historical backlog.
GATE_HANDOFF_SECONDS = 2
HARD_STOP_FREE_GIB = 10
# Refresh expensive guards after preparation, then at least every short batch.
REMOVAL_BATCH_SIZE = 32
GUARD_REUSE_SECONDS = 0.1


def inside(path, root):
    value, prefix = str(path), str(root)
    return value == prefix or value.startswith(prefix.rstrip(os.sep)+os.sep)


def overlaps(a, b):
    return inside(a, b) or inside(b, a)


def task_states(queue):
    """Only an unambiguous landed row permits task-cache removal."""
    states = {}
    for line in queue.read_text().splitlines():
        cells = [cell.strip() for cell in line.strip().strip('|').split('|')]
        if len(cells) >= 4 and re.fullmatch(r'Q\d+', cells[0], re.I):
            key = cells[0].lower()
            states.setdefault(key, set()).add(cells[3].lower())
    return {key: next(iter(values)) if len(values) == 1 else 'unknown'
            for key, values in states.items()}


def active_tasks(queue):
    # Active follow-ups sometimes reuse an older task's notes/cache directory.
    found = set()
    for line in queue.read_text().splitlines():
        cells = [cell.strip() for cell in line.strip().strip('|').split('|')]
        if len(cells) >= 4 and cells[3].lower() == 'active':
            found.update(value.lower() for value in re.findall(r'\bQ\d+\b', line, re.I))
    return found


def command_paths(command, *, allow_root=True):
    """Extract absolute arguments, including quoted shell bodies, without execution."""
    pending, seen = [command], set()
    while pending:
        text = pending.pop()
        if text in seen:
            continue
        seen.add(text)
        try:
            lexer = shlex.shlex(text, posix=True, punctuation_chars=';&|<>()')
            lexer.whitespace_split = True
            lexer.commenters = ''
            tokens = list(lexer)
        except ValueError:
            tokens = text.split()
        for token in tokens:
            # An equals sign can be part of an absolute filename.
            assignment = re.match(r'^(?:--?[\w-]+|[A-Za-z_]\w*)=', token)
            value = token.split('=', 1)[-1] if assignment else token
            if value.startswith('/') and (value != '/' or allow_root):
                yield Path(value)
                if value.rstrip(',;') != value:
                    yield Path(value.rstrip(',;'))
            if '/' in value and (any(c.isspace() for c in value) or
                                 any(c in value for c in ';&|<>()')):
                pending.append(value)


def suite_references(value):
    """Read path-valued metadata and complete command arguments, not summaries."""
    if isinstance(value, dict):
        for key, child in value.items():
            if key == 'command' and isinstance(child, str):
                # Older summaries shortened commands. Step logs retain them whole.
                if len(child) >= 400:
                    child = child.rsplit(' ', 1)[0]
                yield from command_paths(child)
            elif key not in ('summary', 'continuation_summary'):
                yield from suite_references(child)
    elif isinstance(value, list):
        for child in value:
            yield from suite_references(child)
    elif isinstance(value, str) and value.startswith('/'):
        yield Path(value)


def promoted_suite_paths(cache):
    """Current promoted suite and its durable metadata, logs and link targets."""
    summary = cache/'agents/baseline/summary.json'
    if not summary.exists():
        return set(), set()
    value = json.loads(summary.read_text())
    label = value.get('label', '')
    if not isinstance(label, str) or Path(label).name != label or not label:
        raise ValueError('promoted suite label is not a directory name')
    roots = {summary.parent, cache/'agents/suite'/label, cache/'validation/suite'/label}
    if value.get('work'):
        work = Path(value['work'])
        if not work.is_absolute():
            raise ValueError('promoted suite work path must be absolute')
        roots.add(work)
    protected = set(roots)
    protected.update(suite_references(value))
    def walk_error(error):
        raise error

    for root in roots:
        if not root.exists():
            continue
        for directory, directories, files in os.walk(root, followlinks=False, onerror=walk_error):
            for name in [*directories, *files]:
                path = Path(directory)/name
                if path.is_symlink():
                    protected.update((path, path.resolve()))
                elif path.suffix == '.json':
                    protected.update(suite_references(json.loads(path.read_text())))
                elif path.suffix == '.log':
                    # Stream full logs: drivers can print output paths that are
                    # implicit in their command's label rather than arguments.
                    with path.open(errors='replace') as stream:
                        for line in stream:
                            if '/' not in line:
                                continue
                            protected.update(command_paths(line, allow_root=line.startswith('$ ')))
    return {p.resolve() for p in protected} | protected, {p.resolve() for p in roots}


def gone(error):
    return error.errno in (errno.ENOENT, errno.ESRCH, errno.ENOTDIR)


def children(path):
    try:
        return sorted(path.iterdir())
    except OSError as error:
        if gone(error):
            return []
        raise


def process_paths(proc=Path('/proc'), *, uid=None, privileged=True, exclude_pid=None):
    """Open descriptors, mapped files, cwd, executables and pending commands.

    Other users cannot access private caches. Fail closed for unreadable processes
    of this user, except processes that exited during discovery.
    """
    if not (proc/'self/stat').is_file():
        raise OSError('janitor requires Linux /proc process inspection')
    uid = os.getuid() if uid is None else uid
    found = set()
    for process in proc.glob('[0-9]*'):
        try:
            if process.stat().st_uid != uid or int(process.name) in (os.getpid(), exclude_pid):
                continue
            cwd = Path(os.readlink(process/'cwd'))
            paths = [cwd, Path(os.readlink(process/'exe'))]
            for descriptor in (process/'fd').iterdir():
                try:
                    paths.append(Path(os.readlink(descriptor)))
                except OSError as error:
                    if not gone(error):
                        raise
            for line in (process/'maps').read_text().splitlines():
                fields = line.split(maxsplit=5)
                if len(fields) == 6 and fields[5].startswith('/'):
                    paths.append(Path(fields[5].removesuffix(' (deleted)')))
            arguments = (process/'cmdline').read_bytes().split(b'\0')
            variables = (process/'environ').read_bytes().split(b'\0')
            for raw in [*arguments, *variables]:
                value = os.fsdecode(raw).split('=', 1)[-1]
                # Colon-separated paths also cover library/search environments.
                for part in value.split(os.pathsep):
                    if part.startswith('/'):
                        paths.append(Path(part))
                    elif part and '/' in part and not part.startswith('-'):
                        paths.append(cwd/part)
            found.update(p.resolve() for p in paths if p.is_absolute())
        except OSError as error:
            if gone(error) or not process.exists():
                continue
            if isinstance(error, PermissionError) and privileged:
                # Some same-uid container processes are not dumpable. Inspect paths
                # with a narrow read-only helper if passwordless sudo is available;
                # otherwise fail closed instead of silently ignoring that process.
                command = ['sudo', '-n', sys.executable, str(Path(__file__).resolve()),
                           '--process-paths-only', str(uid), '--exclude-process', str(os.getpid())]
                result = subprocess.run(command, capture_output=True, text=True, timeout=30)
                if result.returncode == 0:
                    return {Path(value) for value in json.loads(result.stdout)}
                raise OSError(f'cannot inspect live process {process.name}; read-only helper failed: {result.stderr.strip()}') from error
            raise OSError(f'cannot inspect live process {process.name}: {error}') from error
    return found


class ReferenceChanged(OSError):
    """Monitoring lost its current view; no deletion until discovery succeeds."""


class ReferenceWatch:
    """Keep link discovery current while workers create outputs concurrently.

    Watch before reading each directory. A queue overflow or a directory moved
    outside watched roots invalidates discovery and stops cleanup. Existing destinations stay
    protected even if their original link disappears during this run.
    """
    # Linux inotify event masks.
    CREATE, MOVED_FROM, MOVED_TO, ISDIR = 0x100, 0x40, 0x80, 0x40000000
    DELETE_SELF, MOVE_SELF, OVERFLOW, IGNORED = 0x400, 0x800, 0x4000, 0x8000
    ONLYDIR, NOFOLLOW = 0x1000000, 0x2000000

    def __init__(self, excluded=(), metadata_roots=()):
        self.libc = ctypes.CDLL(None, use_errno=True)
        self.fd = self.libc.inotify_init1(os.O_NONBLOCK | os.O_CLOEXEC)
        if self.fd < 0:
            raise OSError(ctypes.get_errno(), 'cannot monitor reference directories')
        self.directories, self.references, self.retired = {}, set(), set()
        self.moves, self.handled_moves, self.invalidated = {}, set(), set()
        self.excluded = tuple(p.resolve() for p in excluded)
        self.metadata_roots = set(metadata_roots)
        self.metadata_dirty = False
        self.unresolved, self.file_moves = {}, {}

    def close(self):
        if self.fd >= 0:
            os.close(self.fd)
            self.fd = -1

    def add(self, directory):
        mask = self.CREATE | self.MOVED_FROM | self.MOVED_TO | self.DELETE_SELF | self.MOVE_SELF | self.ONLYDIR | self.NOFOLLOW
        if any(inside(directory, root) for root in self.metadata_roots):
            mask |= 0x2 | 0x4 | 0x8 | 0x200  # content, attributes, close-write, delete
        descriptor = self.libc.inotify_add_watch(self.fd, os.fsencode(directory), mask)
        if descriptor < 0:
            number = ctypes.get_errno()
            if number == errno.ENOENT:
                return
            raise OSError(number, f'cannot monitor reference directory {directory}; cleanup deferred')
        previous = self.directories.get(descriptor)
        if previous is not None and previous != directory:
            self.relocate(previous, directory, handled=False)
        if descriptor in self.unresolved:
            old = self.unresolved.pop(descriptor)
            self.invalidated.add(directory)
            self.moves = {cookie: source for cookie, source in self.moves.items() if source != old}
        self.directories[descriptor] = directory

    def relocate(self, source, destination, *, handled=True):
        # A parent rename changes the location of every descendant watch. Rescan
        # the moved tree too: existing relative links may now name new targets.
        for descriptor, previous in self.directories.items():
            if inside(previous, source):
                self.directories[descriptor] = destination/previous.relative_to(source)
                if previous == source and handled:
                    self.handled_moves.add(descriptor)
        self.invalidated.update((source, destination))

    def poll(self):
        pending = set()
        while True:
            try:
                data = os.read(self.fd, 1024*1024)
            except BlockingIOError:
                break
            offset = 0
            while offset < len(data):
                descriptor, mask, cookie, length = struct.unpack_from('iIII', data, offset)
                name = os.fsdecode(data[offset+16:offset+16+length].split(b'\0', 1)[0])
                offset += 16+length
                if mask & self.OVERFLOW:
                    raise ReferenceChanged('reference change queue overflowed; batch deferred')
                directory = self.directories.get(descriptor)
                if directory is None or any(inside(directory, p) for p in self.retired):
                    continue
                if any(inside(directory, root) for root in self.metadata_roots):
                    self.metadata_dirty = True
                if mask & self.DELETE_SELF:
                    # Its links are already protected. The watched parent catches
                    # any replacement directory and its new links.
                    self.directories.pop(descriptor, None)
                    old = self.unresolved.pop(descriptor, directory)
                    self.moves = {cookie: source for cookie, source in self.moves.items() if source != old}
                    continue
                if mask & self.MOVE_SELF:
                    if descriptor in self.handled_moves:
                        self.handled_moves.remove(descriptor)
                        continue
                    self.unresolved[descriptor] = directory
                    continue
                if mask & self.IGNORED:
                    self.directories.pop(descriptor, None)
                    continue
                path = directory/name
                if mask & self.MOVED_FROM:
                    if mask & self.ISDIR:
                        self.moves[cookie] = path
                    else:
                        self.file_moves[cookie] = path
                    continue
                # The destination may have moved again or been deleted by now.
                # Pair recorded directory events before inspecting current paths.
                if mask & self.MOVED_TO:
                    self.file_moves.pop(cookie, None)
                if mask & self.MOVED_TO and mask & self.ISDIR:
                    source = self.moves.pop(cookie, None)
                    if source is not None:
                        self.relocate(source, path)
                try:
                    if path.is_symlink():
                        raw = path.readlink()
                        target = raw if raw.is_absolute() else path.parent/raw
                        self.references.update((Path(os.path.abspath(target)), target.resolve()))
                        if target.is_dir() and not any(inside(target.resolve(), p) for p in self.excluded):
                            pending.add(target.resolve())
                    elif path.is_dir() and not any(inside(path, p) for p in self.excluded):
                        self.add(path)
                        pending.add(path)
                except OSError as error:
                    if not gone(error):
                        raise
        return pending


def reference_paths(roots, *, excluded=(), seconds=300, watcher=None, index_root=None, prune=True):
    """Resolve links at any depth, including directory aliases and link chains.

    A link within a removal candidate conservatively keeps its target too. We
    inspect configured caches and all checkouts; installations and compiler stores
    are excluded. An incomplete index walk permits no deletion.
    """
    found, visited = set(), {}
    excluded = set(p.resolve() for p in excluded)
    roots = list(dict.fromkeys(Path(p).absolute() for p in roots))
    # One shared cache index for every scan root. No index files in checkouts.
    index = DirectoryIndex(index_root or roots[0], seconds=seconds)
    try:
        pending = [root for root in roots if root.exists()]
        while pending:
            batch = pending[-index.batch_size:]
            del pending[-index.batch_size:]
            if watcher:
                for directory in batch:
                    watcher.add(directory)
            for directory, info in zip(batch, index.stat_many(batch)):
                if isinstance(info, OSError):
                    if gone(info):
                        continue
                    raise info
                identity = (info.st_dev, info.st_ino)
                previous = visited.get(identity)
                if previous is not None:
                    if previous == directory:
                        continue
                    try:
                        previous_info = previous.stat()
                    except OSError as error:
                        if not gone(error):
                            raise
                    else:
                        if (previous_info.st_dev, previous_info.st_ino) == identity:
                            continue
                try:
                    entries = index.entries(directory, info)
                except OSError as error:
                    if watcher and gone(error):
                        continue
                    if watcher and str(error).startswith('run-cache directory changed during indexing:'):
                        pending.append(directory)
                        continue
                    raise
                visited[identity] = directory
                for entry in entries:
                    path = directory/entry.name
                    if entry.is_symlink():
                        # Keep lexical and resolved destinations. Lexical paths
                        # protect intermediate links in a chain too.
                        try:
                            target = Path(os.readlink(path))
                        except OSError as error:
                            if watcher and gone(error):
                                continue
                            raise
                        target = target if target.is_absolute() else path.parent/target
                        found.update((Path(os.path.abspath(target)), target.resolve()))
                        if target.is_dir() and not any(inside(target.resolve(), p) for p in excluded):
                            pending.append(target.resolve())
                    elif entry.is_dir() and path not in excluded:
                        pending.append(path)
            if watcher:
                pending.extend(watcher.poll())
                if watcher.invalidated:
                    stale = [key for key, path in visited.items()
                             if any(inside(path, root) for root in watcher.invalidated)]
                    for key in stale:
                        del visited[key]
                    watcher.invalidated.clear()
        if watcher:
            found.update(watcher.references)
            if watcher.unresolved or watcher.moves or watcher.file_moves:
                raise ReferenceChanged('reference directory moved or link names left the discovered view; remaining work retained')
        if prune:
            index.finish()
    finally:
        index.close()
    return found


def inventory(path, directory_ctimes=None):
    """Newest descendant and allocated/reclaimable bytes, never follow links.

    Count a hardlinked inode as reclaimable only if all of its names are here.
    Never chmod a hardlinked file: that would change live targets sharing it.
    """
    newest, allocated, inodes = 0, 0, {}
    device = path.lstat().st_dev
    pending = [path]
    while pending:
        current = pending.pop()
        info = current.lstat()
        if info.st_dev != device or stat.S_ISDIR(info.st_mode) and current.is_mount():
            raise OSError(f'mount boundary inside candidate: {current}')
        key = (info.st_dev, info.st_ino)
        ctime = info.st_ctime
        changed = (directory_ctimes or {}).get(key)
        if stat.S_ISDIR(info.st_mode) and changed and info.st_ctime_ns == changed[0]:
            ctime = changed[1]
        newest = max(newest, info.st_mtime, ctime)
        if key not in inodes:
            allocated += info.st_blocks*512
            inodes[key] = [info, 0]
        inodes[key][1] += 1
        if stat.S_ISDIR(info.st_mode):
            pending.extend(current.iterdir())
    reclaimable = sum(info.st_blocks*512 for info, count in inodes.values()
                      if stat.S_ISDIR(info.st_mode) or info.st_nlink == count)
    return newest, allocated, reclaimable


class Janitor:
    def __init__(self, repo, *, apply=False, idle_hours=DEFAULT_IDLE_HOURS,
                 run_days=DEFAULT_RUN_DAYS, log=None, extra_protected=(), scan_seconds=DEFAULT_SCAN_SECONDS,
                 run_seconds=DEFAULT_RUN_SECONDS):
        self.repo = Path(repo).resolve()
        self.cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'
        self.builds = build.build_root()
        self.volume = self.builds.parent
        self.runtimes = runtime_snapshot.cache_root().resolve()
        self.apply, self.idle_hours, self.run_days = apply, idle_hours, run_days
        self.log, self.scan_seconds = log, scan_seconds
        self.protected = [Path(p).resolve() for p in extra_protected]
        self.removed = self.reclaimable = 0
        self.watch = None
        self.suite_paths, self.suite_roots = set(), set()
        self.baseline_signature = None
        self.run_seconds, self.deadline = run_seconds, None
        self.partial = self.stopped = False
        self.needs_discovery = True
        self.discovery_attempts = self.batch_number = 0

    def emit(self, action, **fields):
        line = json.dumps({'time': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
                           'action': action, **fields}, sort_keys=True)
        print(line, flush=True)
        if self.log:
            self.log.write(line+'\n')
            self.log.flush()

    def discover(self):
        self.refresh_baseline()
        self.states = task_states(self.cache/'agents/QUEUE.md')
        self.active = active_tasks(self.cache/'agents/QUEUE.md')
        checkouts = [entry[0] for entry in worktrees(self.repo)]
        main = main_checkout(self.repo)
        slots = slot_root()
        self.protected += [main, slots, slots.parent/'land-trains', self.repo,
                           *checkouts, *(build.default_target(p, self.builds) for p in [main, self.repo, *checkouts]),
                           *(build.default_target(slots/f'slot-{n:02}', self.builds) for n in range(1, 11))]
        self.protected = list(dict.fromkeys(self.protected))
        self.scan_roots = [self.cache, self.volume, *checkouts, *self.suite_roots]
        self.excluded = [self.builds/'kache', self.runtimes/'snapshots', self.cache/'editors',
                         self.cache/'tools', self.volume/'editors', self.volume/'tools',
                         Path.home()/'Unity/Hub/Editor']
        self.emit('scan', roots=[str(p) for p in self.scan_roots], seconds=self.scan_seconds)
        if self.watch is None:
            self.watch = ReferenceWatch(self.excluded, self.suite_roots)
        else:
            self.watch.metadata_roots.update(self.suite_roots)
        links = reference_paths(self.scan_roots, excluded=self.excluded, seconds=self.scan_budget(),
                                     watcher=self.watch, index_root=self.cache)
        self.links = getattr(self, 'links', set()) | links
        self.refresh_baseline(force=True)
        self.emit('references_ready', watched_directories=len(self.watch.directories), references=len(self.links))
        self.processes = process_paths()

    def candidates(self):
        self.refresh_baseline()
        for path, kind, hours in self.unprotected_candidates():
            if any(overlaps(path, p) for p in self.suite_paths):
                self.emit('keep', path=str(path), reason='promoted sheriff suite reference')
                continue
            yield path, kind, hours

    def unprotected_candidates(self):
        # Probe-named runs/<probe>/ remain live regardless of their age.
        for root in [self.volume/'unbound-validation/runs']:
            if root.is_dir():
                for path in children(root):
                    if path.is_dir() and not path.is_symlink() and (
                            re.search(r'(?<!\d)20\d{6}(?!\d)', path.name) or
                            re.search(r'-t3-[0-9a-f]+', path.name)):
                        yield path, 'run', self.run_days*24
        for root in dict.fromkeys([self.volume, self.cache.resolve()]):
            for path in children(root):
                if not re.fullmatch(r'q\d+', path.name, re.I) or path.is_symlink() or not path.is_dir():
                    continue
                if self.states.get(path.name.lower()) != 'landed':
                    self.emit('keep', path=str(path), reason='task is not landed')
                    continue
                # Keep notes at any depth, including their parent directories.
                for child in children(path):
                    yield from self.task_children(child)
        targets = self.builds/'targets'
        if targets.is_dir():
            for path in children(targets):
                if path.is_dir() and not path.is_symlink():
                    yield path, 'target', self.idle_hours

    def task_children(self, path):
        if re.fullmatch(r'NOTES.*\.md', path.name, re.I):
            return
        if path.is_dir() and not path.is_symlink() and any(
                re.fullmatch(r'NOTES.*\.md', p.name, re.I) for p in path.rglob('NOTES*.md')):
            for child in children(path):
                yield from self.task_children(child)
        else:
            yield path, 'task', self.idle_hours

    def protection_reason(self, path):
        if any(overlaps(path, p) for p in self.suite_paths):
            return 'promoted sheriff suite reference'
        if any(value.lower() in self.active for part in path.parts
               for value in re.findall(r'(?<![a-zA-Z0-9])q\d+(?!\d)', part, re.I)):
            return 'active task directory'
        if any(overlaps(path, p) for p in self.protected):
            return 'protected checkout, slot or train'
        if any(inside(p, path) for p in self.links):
            return 'symlink reference'
        if any(inside(p, path) for p in self.processes):
            return 'live process'
        return None

    def reason(self, path, hours, directory_ctimes=None):
        if reason := self.protection_reason(path):
            return reason
        newest, _, _ = (inventory(path, directory_ctimes=directory_ctimes)
                        if directory_ctimes else inventory(path))
        if newest >= time.time()-hours*3600:
            return 'recently modified'
        return None

    def refresh_baseline(self, force=False):
        summary = self.cache/'agents/baseline/summary.json'
        try:
            info = summary.stat()
            signature = (info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns, info.st_size)
        except FileNotFoundError:
            signature = None
        if force or signature != self.baseline_signature:
            protected, roots = promoted_suite_paths(self.cache)
            # A promotion never unprotects the prior baseline during this run.
            self.suite_paths.update(protected)
            new_roots = roots-self.suite_roots
            self.suite_roots.update(roots)
            self.baseline_signature = signature
            if self.watch and new_roots:
                self.watch.metadata_roots.update(new_roots)
                self.links.update(reference_paths(new_roots, excluded=self.excluded, seconds=self.scan_budget(),
                                                  watcher=self.watch, index_root=self.cache, prune=False))
                for directory in list(self.watch.directories.values()):
                    if any(inside(directory, root) for root in new_roots):
                        self.watch.add(directory)

    def refresh_links(self):
        if self.watch is None:
            return
        pending = self.watch.poll()
        if pending or self.watch.unresolved or self.watch.moves or self.watch.file_moves:
            full = bool(self.watch.unresolved or self.watch.moves or self.watch.file_moves)
            roots = self.scan_roots if full else pending
            try:
                links = reference_paths(roots, excluded=self.excluded, seconds=self.scan_budget(),
                                        watcher=self.watch, index_root=self.cache, prune=False)
            except ReferenceChanged:
                if full:
                    raise
                links = reference_paths(self.scan_roots, excluded=self.excluded, seconds=self.scan_budget(),
                                        watcher=self.watch, index_root=self.cache, prune=False)
            self.links.update(links)
        self.links.update(self.watch.references)
        self.refresh_baseline(force=self.watch.metadata_dirty)
        self.watch.metadata_dirty = False

    def prepare_removal(self, path, kind, hours):
        path = Path(os.path.abspath(path))
        hours = max(self.idle_hours, hours or 0)
        try:
            self.refresh_links()
            if reason := self.reason(path, hours):
                self.emit('keep', path=str(path), reason=reason)
                return None
            _, allocated, reclaimable = inventory(path)
            directory = path.is_dir() and not path.is_symlink()
            directory_ctimes = {}
            if self.apply and directory:
                # Only directories need chmod; files can share live target inodes.
                for _, _, _, descriptor in os.fwalk(path, follow_symlinks=False):
                    before = os.fstat(descriptor)
                    mode = stat.S_IMODE(before.st_mode)
                    if mode & stat.S_IRWXU != stat.S_IRWXU:
                        os.fchmod(descriptor, mode | stat.S_IRWXU)
                        after = os.fstat(descriptor)
                        directory_ctimes[(before.st_dev, before.st_ino)] = (after.st_ctime_ns, before.st_ctime)
            # Recheck every descendant after permission preparation. Only the
            # exact ctime changes made above are excluded from the age test.
            if reason := self.reason(path, hours, directory_ctimes):
                self.emit('keep', path=str(path), reason=reason)
                return None
            identity = path.lstat()
            task = next((p.name.lower() for p in path.parents
                         if p.parent in (self.volume, self.cache.resolve()) and
                         re.fullmatch(r'q\d+', p.name, re.I)), '')
            return path, kind, directory, task, allocated, reclaimable, hours, (identity.st_dev, identity.st_ino, stat.S_IFMT(identity.st_mode))
        except FileNotFoundError:
            return None

    def refresh_guards(self):
        self.processes = process_paths()
        self.refresh_queue()
        self.refresh_links()
        self.guards_at = time.monotonic()

    def refresh_queue(self):
        self.refresh_baseline()
        queue = self.cache/'agents/QUEUE.md'
        info = queue.stat()
        signature = (info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns, info.st_size)
        if signature != getattr(self, 'queue_signature', None):
            self.states = task_states(queue)
            self.active = active_tasks(queue)
            self.queue_signature = signature

    def remove_batch(self, candidates):
        # Directory preparation may take minutes. Do not hold an earlier file's
        # guards across another tree walk. Tiny files still share guard scans.
        pending, removed = [], 0
        for candidate in candidates:
            path = candidate[0]
            if Path(path).is_dir() and not Path(path).is_symlink():
                if pending:
                    removed += self.remove_prepared_batch(pending)
                    pending.clear()
                removed += self.remove_prepared_batch([candidate])
            else:
                pending.append(candidate)
                if len(pending) >= REMOVAL_BATCH_SIZE:
                    removed += self.remove_prepared_batch(pending)
                    pending.clear()
        return removed+self.remove_prepared_batch(pending)

    def remove_prepared_batch(self, candidates):
        prepared = [value for path, kind, hours in candidates
                    if (value := self.prepare_removal(path, kind, hours)) is not None]
        if not prepared:
            return 0
        if self.apply:
            self.refresh_guards()
        removed = 0
        for path, kind, directory, task, allocated, reclaimable, hours, identity in prepared:
            try:
                if self.apply:
                    # A slow removal ends guard reuse. Queue edits and new links
                    # are checked for every entry, including the first one.
                    if time.monotonic()-self.guards_at >= GUARD_REUSE_SECONDS:
                        self.refresh_guards()
                    self.refresh_queue()
                    self.refresh_links()
                    if kind == 'task' and self.states.get(task) != 'landed':
                        self.emit('keep', path=str(path), reason='task state changed')
                        continue
                    if reason := self.protection_reason(path):
                        self.emit('keep', path=str(path), reason=reason)
                        continue
                    current = path.lstat()
                    if (current.st_dev, current.st_ino, stat.S_IFMT(current.st_mode)) != identity:
                        self.emit('keep', path=str(path), reason='candidate identity changed')
                        continue
                    if not directory and max(current.st_mtime, current.st_ctime) >= time.time()-hours*3600:
                        self.emit('keep', path=str(path), reason='recently modified')
                        continue
                    self.emit('removing', path=str(path), kind=kind, allocated_bytes=allocated,
                              reclaimable_bytes=reclaimable)
                    if self.watch:
                        self.watch.retired.add(path)
                    if directory:
                        shutil.rmtree(path)
                    else:
                        path.unlink()
                self.emit('removed' if self.apply else 'would_remove', path=str(path), kind=kind,
                          allocated_bytes=allocated, reclaimable_bytes=reclaimable)
                self.removed += 1
                removed += 1
                self.reclaimable += reclaimable
            except FileNotFoundError:
                continue
        return removed

    def remove(self, path, *, kind='runtime', hours=None):
        """Runtime collector callback, with a fresh single-entry guard batch."""
        return bool(self.remove_batch([(path, kind, hours)]))

    def scan_budget(self):
        remaining = self.deadline-time.monotonic() if self.deadline is not None else self.scan_seconds
        if remaining <= 0:
            raise TimeoutError('janitor run budget exhausted')
        return min(self.scan_seconds, remaining)

    def stop_batches(self, reason):
        self.partial = self.stopped = True
        self.emit('batch_limit', reason=reason, batches=self.batch_number, removals=self.removed)
        return False

    def ensure_discovery(self):
        while self.needs_discovery:
            if self.discovery_attempts >= 1+MAX_REDISCOVERIES:
                return self.stop_batches('reference recovery limit reached; remaining work retained')
            if self.deadline is not None and time.monotonic() >= self.deadline:
                return self.stop_batches('run budget exhausted; remaining work retained')
            self.discovery_attempts += 1
            self.emit('discovery_start', attempt=self.discovery_attempts)
            try:
                self.discover()
            except ReferenceChanged as error:
                self.emit('discovery_deferred', attempt=self.discovery_attempts, error=str(error))
                return self.stop_batches('reference trees could not be recovered; remaining work retained')
            except TimeoutError as error:
                self.emit('discovery_deferred', attempt=self.discovery_attempts, error=str(error))
            else:
                self.needs_discovery = False
                self.emit('discovery_finish', attempt=self.discovery_attempts)
        return True

    def run_batch(self, candidates):
        if self.stopped or not self.ensure_discovery():
            return None
        if self.deadline is not None and time.monotonic() >= self.deadline:
            self.stop_batches('run budget exhausted; remaining work retained')
            return None
        self.batch_number += 1
        number, started, before = self.batch_number, time.monotonic(), self.removed
        self.emit('batch_start', batch=number, candidates=len(candidates))
        status, error = 'complete', None
        try:
            self.remove_batch(candidates)
        except ReferenceChanged as problem:
            status, error = 'deferred', str(problem)
            self.stop_batches('reference trees could not be recovered; remaining work retained')
        except TimeoutError as problem:
            # Retain the unsafe remainder of this batch. Later batches require a
            # complete fresh scan; previously discovered destinations stay kept.
            self.partial = self.needs_discovery = True
            status, error = 'deferred', str(problem)
        except BaseException as problem:
            status, error = 'failed', str(problem)
            raise
        finally:
            self.emit('batch_finish', batch=number, status=status, error=error,
                      removals=self.removed-before, total_removals=self.removed,
                      seconds=round(time.monotonic()-started, 3))
        return self.removed-before

    def collect(self):
        self.deadline = time.monotonic()+self.run_seconds
        try:
            if self.ensure_discovery():
                self.collect_candidates()
                if not self.stopped:
                    runtime_snapshot.cleanup(days=self.idle_hours/24, cache=self.runtimes, repo=self.repo,
                                             remove=lambda path: bool(self.run_batch([(path, 'runtime', None)])),
                                             dry_run=not self.apply)
            # Cache GC has independent publication locks and held-output rules.
            # A partial directory pass must not starve this phase indefinitely.
            self.kache_gc()
        finally:
            if self.watch:
                self.watch.close()

    def collect_candidates(self):
        pending = []
        for path, kind, hours in self.candidates():
            if kind != 'target':
                pending.append((path, kind, hours))
                if len(pending) >= REMOVAL_BATCH_SIZE:
                    if self.run_batch(pending) is None:
                        return
                    pending.clear()
                continue
            if pending:
                if self.run_batch(pending) is None:
                    return
                pending.clear()
            marker = path/'.unbound-source-root'
            if not marker.is_file() or marker.is_symlink():
                self.emit('keep', path=str(path), reason='no trustworthy checkout marker')
                continue
            try:
                with marker.open() as stream:
                    try:
                        fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
                    except BlockingIOError:
                        self.emit('keep', path=str(path), reason='target is claimed')
                        continue
                    owner = stream.read().strip()
                    if not owner or not Path(owner).is_absolute() or Path(owner).exists():
                        self.emit('keep', path=str(path), reason='checkout exists or marker is invalid')
                        continue
                    if self.run_batch([(path, kind, hours)]) is None:
                        return
            except FileNotFoundError:
                continue
        if pending:
            self.run_batch(pending)

    def kache_gc(self):
        pinned = Path.home()/'.cache/unbound/tools/kache-2.0.0/kache'
        executable = str(pinned) if pinned.is_file() else shutil.which('kache')
        cache = self.builds/'kache'
        if not executable or not cache.is_dir():
            return
        # Size-driven gc has no dry-run mode in kache 2.0. Its own locks protect
        # concurrent builders. Keep hardlinked entries; their blocks belong to
        # live targets, so the registered size may exceed the configured limit.
        command = [executable, 'gc', '--json']
        self.emit('kache_gc' if self.apply else 'would_kache_gc', command=command,
                  max_size='30GiB', note='held target bytes are excluded from the size budget')
        if self.apply:
            env = dict(os.environ, KACHE_CACHE_DIR=str(cache), KACHE_MAX_SIZE='30GiB',
                       KACHE_GC_EVICT_SHARED='0', KACHE_LOCAL_ONLY='1')
            result = subprocess.run(command, env=env, capture_output=True, text=True, timeout=180)
            self.emit('kache_result', returncode=result.returncode, stdout=result.stdout, stderr=result.stderr)
            if result.returncode:
                raise RuntimeError('kache gc failed; see janitor log')


def free_space(repo, protected=()):
    paths = [Path('/'), build.build_root(), Path(repo), *map(Path, protected)]
    found = {}
    for path in paths:
        while not path.exists():
            path = path.parent
        found.setdefault(path.stat().st_dev, (str(path), shutil.disk_usage(path).free))
    return dict(found.values())


def before_gate(repo):
    """Request the timer's serialized service without waiting for cleanup.

    Even below the hard reserve we do no inline deletion: discovery itself can
    exceed a gate's budget. The service applies the normal safety guards.
    """
    try:
        threshold = float(os.environ.get('UNBOUND_JANITOR_MIN_FREE_GIB', DEFAULT_MIN_FREE_GIB))*GIB
        available = free_space(repo)
        if not any(value < threshold for value in available.values()):
            return
        if any(value < HARD_STOP_FREE_GIB*GIB for value in available.values()):
            print('land: disk below 10 GiB hard reserve; requesting urgent asynchronous cleanup', flush=True)
        result = subprocess.run(['systemctl', '--user', 'start', '--no-block', 'unbound-janitor.service'],
                                capture_output=True, text=True, timeout=GATE_HANDOFF_SECONDS)
        if result.returncode:
            print(f'land: disk janitor handoff failed: {result.stderr.strip()}', flush=True)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f'land: disk janitor deferred: {error}', flush=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--process-paths-only', type=int, help=argparse.SUPPRESS)
    parser.add_argument('--exclude-process', type=int, help=argparse.SUPPRESS)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--dry-run', action='store_true', help='default; mutually exclusive with --apply')
    parser.add_argument('--idle-hours', type=float, default=DEFAULT_IDLE_HOURS)
    parser.add_argument('--run-days', type=float, default=DEFAULT_RUN_DAYS)
    parser.add_argument('--scan-seconds', type=float, default=DEFAULT_SCAN_SECONDS)
    parser.add_argument('--run-seconds', type=float, default=DEFAULT_RUN_SECONDS)
    parser.add_argument('--protect', type=Path, action='append', default=[])
    args = parser.parse_args(argv)
    if args.process_paths_only is not None:
        print(json.dumps(sorted(str(p) for p in process_paths(uid=args.process_paths_only, privileged=False, exclude_pid=args.exclude_process))))
        return 0
    if args.apply and args.dry_run or min(args.idle_hours, args.run_days, args.scan_seconds, args.run_seconds) <= 0:
        parser.error('choose one mode and positive retention/scan limits')
    cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'
    logs = cache/'agents/janitor'
    logs.mkdir(parents=True, exist_ok=True)
    with (logs/'lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print('janitor already running; cleanup deferred', flush=True)
            return 0
        path = logs/f'{time.strftime("%Y%m%d-%H%M%S")}-{os.getpid()}.jsonl'
        with path.open('x') as log:
            janitor = Janitor(args.repo, apply=args.apply, idle_hours=args.idle_hours, run_days=args.run_days,
                              log=log, extra_protected=args.protect, scan_seconds=args.scan_seconds, run_seconds=args.run_seconds)
            before = free_space(args.repo, args.protect)
            janitor.emit('start', apply=args.apply, log=str(path), free_bytes=before)
            failed = False
            try:
                janitor.collect()
            except (OSError, ValueError, RuntimeError, sqlite3.Error, subprocess.SubprocessError) as error:
                janitor.emit('error', error=str(error))
                failed = True
            after = free_space(args.repo, args.protect)
            janitor.emit('summary', apply=args.apply, failed=failed, partial=janitor.partial, batches=janitor.batch_number,
                          removals=janitor.removed,
                          reclaimable_bytes=janitor.reclaimable, free_bytes=after,
                          free_change_bytes={p: after[p]-value for p, value in before.items() if p in after})
    return 1 if failed else 3 if janitor.partial else 0


if __name__ == '__main__':
    raise SystemExit(main())
