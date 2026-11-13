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


def gone(error):
    return error.errno in (errno.ENOENT, errno.ESRCH, errno.ENOTDIR)


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


class ReferenceWatch:
    """Keep link discovery current while workers create outputs concurrently.

    Watch before reading each directory. A queue overflow or a moved watched
    directory invalidates discovery and stops cleanup. Existing destinations stay
    protected even if their original link disappears during this run.
    """
    CREATE, MOVED_TO = 0x100, 0x80
    DELETE_SELF, MOVE_SELF, OVERFLOW, IGNORED = 0x400, 0x800, 0x4000, 0x8000
    ONLYDIR, NOFOLLOW = 0x1000000, 0x2000000

    def __init__(self, excluded=()):
        self.libc = ctypes.CDLL(None, use_errno=True)
        self.fd = self.libc.inotify_init1(os.O_NONBLOCK | os.O_CLOEXEC)
        if self.fd < 0:
            raise OSError(ctypes.get_errno(), 'cannot monitor reference directories')
        self.directories, self.references, self.retired = {}, set(), set()
        self.excluded = tuple(p.resolve() for p in excluded)

    def close(self):
        if self.fd >= 0:
            os.close(self.fd)
            self.fd = -1

    def add(self, directory):
        mask = self.CREATE | self.MOVED_TO | self.DELETE_SELF | self.MOVE_SELF | self.ONLYDIR | self.NOFOLLOW
        descriptor = self.libc.inotify_add_watch(self.fd, os.fsencode(directory), mask)
        if descriptor < 0:
            number = ctypes.get_errno()
            if number == errno.ENOENT:
                return
            raise OSError(number, f'cannot monitor reference directory {directory}; cleanup deferred')
        self.directories[descriptor] = directory

    def poll(self):
        pending = set()
        while True:
            try:
                data = os.read(self.fd, 1024*1024)
            except BlockingIOError:
                break
            offset = 0
            while offset < len(data):
                descriptor, mask, _, length = struct.unpack_from('iIII', data, offset)
                name = os.fsdecode(data[offset+16:offset+16+length].split(b'\0', 1)[0])
                offset += 16+length
                if mask & self.OVERFLOW:
                    raise OSError('reference change queue overflowed; cleanup deferred')
                directory = self.directories.get(descriptor)
                if directory is None or any(inside(directory, p) for p in self.retired):
                    continue
                if mask & self.DELETE_SELF:
                    # Its links are already protected. The watched parent catches
                    # any replacement directory and its new links.
                    self.directories.pop(descriptor, None)
                    continue
                if mask & self.MOVE_SELF:
                    raise OSError(f'reference directory moved: {directory}; cleanup deferred')
                if mask & self.IGNORED:
                    self.directories.pop(descriptor, None)
                    continue
                path = directory/name
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
    found, visited = set(), set()
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
                if identity in visited:
                    continue
                try:
                    entries = index.entries(directory, info)
                except OSError as error:
                    if watcher and str(error).startswith('run-cache directory changed during indexing:'):
                        pending.append(directory)
                        continue
                    raise
                visited.add(identity)
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
        if watcher:
            found.update(watcher.references)
        if prune:
            index.finish()
    finally:
        index.close()
    return found


def inventory(path):
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
        newest = max(newest, info.st_mtime, info.st_ctime)
        key = (info.st_dev, info.st_ino)
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
                 run_days=DEFAULT_RUN_DAYS, log=None, extra_protected=(), scan_seconds=DEFAULT_SCAN_SECONDS):
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

    def emit(self, action, **fields):
        line = json.dumps({'time': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
                           'action': action, **fields}, sort_keys=True)
        print(line, flush=True)
        if self.log:
            self.log.write(line+'\n')
            self.log.flush()

    def discover(self):
        self.states = task_states(self.cache/'agents/QUEUE.md')
        self.active = active_tasks(self.cache/'agents/QUEUE.md')
        checkouts = [entry[0] for entry in worktrees(self.repo)]
        main = main_checkout(self.repo)
        slots = slot_root()
        self.protected += [main, slots, slots.parent/'land-trains', self.repo,
                           *checkouts, *(build.default_target(p, self.builds) for p in [main, self.repo, *checkouts]),
                           *(build.default_target(slots/f'slot-{n:02}', self.builds) for n in range(1, 11))]
        self.scan_roots = [self.cache, self.volume, *checkouts]
        self.excluded = [self.builds/'kache', self.runtimes/'snapshots', self.cache/'editors',
                         self.cache/'tools', self.volume/'editors', self.volume/'tools',
                         Path.home()/'Unity/Hub/Editor']
        self.emit('scan', roots=[str(p) for p in self.scan_roots], seconds=self.scan_seconds)
        self.watch = ReferenceWatch(self.excluded)
        self.links = reference_paths(self.scan_roots, excluded=self.excluded, seconds=self.scan_seconds,
                                     watcher=self.watch, index_root=self.cache)
        self.emit('references_ready', watched_directories=len(self.watch.directories), references=len(self.links))
        self.processes = process_paths()

    def candidates(self):
        # Probe-named runs/<probe>/ remain live regardless of their age.
        for root in [self.volume/'unbound-validation/runs']:
            if root.is_dir():
                for path in sorted(root.iterdir()):
                    if path.is_dir() and not path.is_symlink() and (
                            re.search(r'(?<!\d)20\d{6}(?!\d)', path.name) or
                            re.search(r'-t3-[0-9a-f]+', path.name)):
                        yield path, 'run', self.run_days*24
        for root in dict.fromkeys([self.volume, self.cache.resolve()]):
            for path in sorted(root.iterdir()):
                if not re.fullmatch(r'q\d+', path.name, re.I) or path.is_symlink() or not path.is_dir():
                    continue
                if self.states.get(path.name.lower()) != 'landed':
                    self.emit('keep', path=str(path), reason='task is not landed')
                    continue
                # Keep notes at any depth, including their parent directories.
                for child in sorted(path.iterdir()):
                    yield from self.task_children(child)
        targets = self.builds/'targets'
        if targets.is_dir():
            for path in sorted(targets.iterdir()):
                if path.is_dir() and not path.is_symlink():
                    yield path, 'target', self.idle_hours

    def task_children(self, path):
        if re.fullmatch(r'NOTES.*\.md', path.name, re.I):
            return
        if path.is_dir() and not path.is_symlink() and any(
                re.fullmatch(r'NOTES.*\.md', p.name, re.I) for p in path.rglob('NOTES*.md')):
            for child in sorted(path.iterdir()):
                yield from self.task_children(child)
        else:
            yield path, 'task', self.idle_hours

    def protection_reason(self, path):
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

    def reason(self, path, hours):
        if reason := self.protection_reason(path):
            return reason
        newest, _, _ = inventory(path)
        if newest >= time.time()-hours*3600:
            return 'recently modified'
        return None

    def refresh_links(self):
        if self.watch is None:
            return
        pending = self.watch.poll()
        if pending:
            self.links.update(reference_paths(pending, excluded=self.excluded, seconds=self.scan_seconds,
                                              watcher=self.watch, index_root=self.cache, prune=False))
        self.links.update(self.watch.references)

    def remove(self, path, *, kind='runtime', hours=None):
        """Runtime collector callback; re-check each candidate immediately before removal."""
        path = Path(os.path.abspath(path))
        hours = max(self.idle_hours, hours or 0)
        try:
            self.refresh_links()
            if reason := self.reason(path, hours):
                self.emit('keep', path=str(path), reason=reason)
                return False
            _, allocated, reclaimable = inventory(path)
            if self.apply:
                # Refresh process/queue guards after a potentially long discovery.
                self.processes = process_paths()
                self.states = task_states(self.cache/'agents/QUEUE.md')
                self.active = active_tasks(self.cache/'agents/QUEUE.md')
                if kind == 'task':
                    task = next((p.name.lower() for p in path.parents
                                 if p.parent in (self.volume, self.cache.resolve()) and re.fullmatch(r'q\d+', p.name, re.I)), '')
                    if self.states.get(task) != 'landed':
                        self.emit('keep', path=str(path), reason='task state changed')
                        return False
                self.refresh_links()
                if reason := self.reason(path, hours):
                    self.emit('keep', path=str(path), reason=reason)
                    return False
                directory = path.is_dir() and not path.is_symlink()
                if directory:
                    # Read-only snapshot files need no chmod; only directories do.
                    for _, _, _, descriptor in os.fwalk(path, follow_symlinks=False):
                        os.fchmod(descriptor, stat.S_IMODE(os.fstat(descriptor).st_mode) | stat.S_IRWXU)
                # Inventory and permission preparation can walk a huge tree.
                # Recheck after both, with no further tree walk before deletion.
                self.processes = process_paths()
                self.states = task_states(self.cache/'agents/QUEUE.md')
                self.active = active_tasks(self.cache/'agents/QUEUE.md')
                self.refresh_links()
                if kind == 'task' and self.states.get(task) != 'landed':
                    self.emit('keep', path=str(path), reason='task state changed')
                    return False
                if reason := self.protection_reason(path):
                    self.emit('keep', path=str(path), reason=reason)
                    return False
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
            self.reclaimable += reclaimable
            return True
        except FileNotFoundError:
            return False

    def collect(self):
        try:
            self.discover()
            self.collect_candidates()
        finally:
            if self.watch:
                self.watch.close()

    def collect_candidates(self):
        for path, kind, hours in self.candidates():
            if kind == 'target':
                marker = path/'.unbound-source-root'
                if not marker.is_file() or marker.is_symlink():
                    self.emit('keep', path=str(path), reason='no trustworthy checkout marker')
                    continue
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
                    self.remove(path, kind=kind, hours=hours)
            else:
                self.remove(path, kind=kind, hours=hours)
        # The collector owns its publication lock and pin/reference rules. The same
        # age, process and external-link guards apply to each of its removals.
        runtime_snapshot.cleanup(days=self.idle_hours/24, cache=self.runtimes, repo=self.repo,
                                 remove=lambda path: self.remove(path), dry_run=not self.apply)
        self.kache_gc()

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
    """Called only before a fresh gate; never fail a gate because cleanup deferred."""
    threshold = float(os.environ.get('UNBOUND_JANITOR_MIN_FREE_GIB', DEFAULT_MIN_FREE_GIB))*GIB
    if any(value < threshold for value in free_space(repo).values()):
        try:
            main(['--repo', str(repo), '--apply', '--protect', str(repo)])
        except (OSError, ValueError, RuntimeError, sqlite3.Error, subprocess.SubprocessError) as error:
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
    parser.add_argument('--protect', type=Path, action='append', default=[])
    args = parser.parse_args(argv)
    if args.process_paths_only is not None:
        print(json.dumps(sorted(str(p) for p in process_paths(uid=args.process_paths_only, privileged=False, exclude_pid=args.exclude_process))))
        return 0
    if args.apply and args.dry_run or min(args.idle_hours, args.run_days, args.scan_seconds) <= 0:
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
                              log=log, extra_protected=args.protect, scan_seconds=args.scan_seconds)
            before = free_space(args.repo, args.protect)
            janitor.emit('start', apply=args.apply, log=str(path), free_bytes=before)
            failed = False
            try:
                janitor.collect()
            except (OSError, ValueError, RuntimeError, sqlite3.Error, subprocess.SubprocessError) as error:
                janitor.emit('error', error=str(error))
                failed = True
            after = free_space(args.repo, args.protect)
            janitor.emit('summary', apply=args.apply, failed=failed, removals=janitor.removed,
                          reclaimable_bytes=janitor.reclaimable, free_bytes=after,
                          free_change_bytes={p: after[p]-value for p, value in before.items() if p in after})
    return int(failed)


if __name__ == '__main__':
    raise SystemExit(main())
