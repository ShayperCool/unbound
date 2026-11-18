#!/usr/bin/env python3
"""Conservative disk hygiene for validation runs, suite labels, completed tasks and orphan targets.

Default is dry-run. Use --apply after inspecting it. JSON lines go to stdout and
agents/janitor/<timestamp>.jsonl: what is removed (or would be), errors and state, and a summary
that counts what was kept by reason (an hourly run keeps thousands of paths; one line each filled
the system log). No worktree or live checkout target is removed.

Workers write into the cache all the time (a live Editor rotates its Library folders), so
nothing here depends on a stable view of the whole cache: one walk lists every link (a directory
that vanishes or changes meanwhile is skipped or listed again), and a candidate is removed only
when nothing protects it then and again right before its removal. A candidate is a directory
whose every file is older than its kind's age (24 hours for validation runs, 4 days for suite
labels). A link created into such a directory after the walk is the one case the walk cannot
see; the walk is repeated every LINK_SNAPSHOT_SECONDS while removals continue, and live
processes are read again at least every PROCESS_GUARD_SECONDS.
"""
import argparse
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
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build
import processes
from slot import main_checkout, slot_root, worktrees
from run_cache_index import DirectoryChanged, DirectoryIndex, Entry
import runtime_snapshot

GIB = 1024**3
# Validation run instances and suite labels are kept this long after their last write.
DEFAULT_RUN_HOURS = 24
DEFAULT_SUITE_DAYS = 4
# Landed task caches, orphan targets and runtimes: the last-write grace of every other removal.
DEFAULT_IDLE_HOURS = 24
DEFAULT_MIN_FREE_GIB = 20
# The link walk of the measured cache (about 2.3 million directories) took 8-37 minutes on the
# loaded machine. The defaults fit a 45-minute service limit; install_janitor.py installs the
# hourly service with a 60-minute limit and passes SERVICE_SCAN_SECONDS / SERVICE_RUN_SECONDS.
DEFAULT_SCAN_SECONDS = 1800
DEFAULT_RUN_SECONDS = 2100
SERVICE_SCAN_SECONDS = 2700
SERVICE_RUN_SECONDS = 3000
# Links found by a walk protect removals for this long; the walk is repeated after it.
LINK_SNAPSHOT_SECONDS = 900
# The runtime collector (seconds) runs on the walk taken before it, walked again first when older
# than this, so that it stays within LINK_SNAPSHOT_SECONDS without walking under its lock.
RUNTIME_WALK_SECONDS = 600
# Reading every live process's paths takes 4-8 s on the loaded machine; removals reuse one
# reading for at most this long (every candidate is idle for a day or more).
PROCESS_GUARD_SECONDS = 60
# Time per run for reading sheriff suites' and agent records' JSON and log files that are new or
# changed since an earlier run read them (a finished suite holds about 2,000 such files, 250 MB).
SUITE_REFERENCE_SECONDS = 900
# An absolute path through a runs/ or suite/ directory in suite logs, JSON and agent records;
# resolved, those that lead into the validation tree are kept (they are often written through
# linked prefixes).
VALIDATION_REFERENCE = re.compile(rb'/[^\s"\'<>|;&(),=]*/(?:runs|suite)/[^\s"\'<>|;&(),]+')
# The cache of those references: its entries are dropped when this changes.
SUITE_REFERENCE_FORMAT = 5
# Keep land's lock independent of a service manager or a historical backlog.
GATE_HANDOFF_SECONDS = 2
HARD_STOP_FREE_GIB = 10
# A run instance or suite label with one of these in its name is never a candidate.
RETAINED_NAMES = ('sheriff', 'baseline')
# Times the link walk lists again the places that changed while it walked them.
RECHECK_ROUNDS = 3


def inside(path, root):
    value, prefix = str(path), str(root)
    return value == prefix or value.startswith(prefix.rstrip(os.sep)+os.sep)


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


def json_references(text):
    """References in a JSON document, or in JSON Lines such as com.unity.logging's
    Output.log.json, where every line is one document. An empty file names nothing.
    Anything else that does not parse raises: its references are unknown."""
    if not text.strip():
        return []
    try:
        return list(suite_references(json.loads(text)))
    except json.JSONDecodeError as error:
        # Only \n ends a record: JSON strings may hold raw U+2028/U+2029.
        lines = [line for line in text.split('\n') if line.strip()]
        if len(lines) < 2:
            raise
        found = []
        for line in lines:
            try:
                found.extend(suite_references(json.loads(line)))
            except json.JSONDecodeError:
                raise error from None
        return found


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
                # lstat raises on unreadable or vanished entries, unlike is_file(),
                # so discovery stops and the next run retries.
                mode = os.lstat(path).st_mode
                if stat.S_ISLNK(mode):
                    protected.update((path, path.resolve()))
                elif not stat.S_ISREG(mode):
                    # Directories named *.json (report.json/) are walked themselves;
                    # FIFOs and sockets hold no references.
                    continue
                elif path.suffix == '.json':
                    protected.update(json_references(path.read_text()))
                elif path.suffix == '.log':
                    # Stream full logs: drivers can print output paths that are
                    # implicit in their command's label rather than arguments.
                    with path.open(errors='replace') as stream:
                        for line in stream:
                            if '/' not in line:
                                continue
                            protected.update(command_paths(line, allow_root=line.startswith('$ ')))
    return {p.resolve() for p in protected} | protected, {p.resolve() for p in roots}



def validation_references(roots, validation, known, seconds):
    """Paths under the validation tree (runs/ and suite/, resolved) named by the JSON, JSON Lines
    and log files under roots (sheriff suites and agent records), with the files read before
    (known: {file: [inode, size, mtime_ns, ctime_ns, [references]]}) read again only when they
    changed. Returns (references, files now known, complete). Stops reading at the time budget;
    an incomplete read names only part of the references."""
    deadline = time.monotonic()+seconds
    validation = os.path.realpath(validation)
    files, found, complete = {}, set(), True
    def walk_error(error):
        if not gone(error):
            raise error
    for root in roots:
        for directory, directories, names in os.walk(os.path.realpath(root), followlinks=False,
                                                     onerror=walk_error):
            for name in names:
                if not name.endswith(('.json', '.jsonl', '.log')):
                    continue
                path = os.path.join(directory, name)
                try:
                    info = os.stat(path)  # a linked file is read through its link
                except OSError as error:
                    if gone(error):
                        continue
                    raise
                if not stat.S_ISREG(info.st_mode):
                    continue
                identity = [info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns]
                entry = known.get(path)
                if entry is None or entry[:4] != identity:
                    if time.monotonic() > deadline:
                        complete = False
                        continue
                    with open(path, 'rb') as stream:
                        named = {os.fsdecode(match) for match in VALIDATION_REFERENCE.findall(stream.read())}
                    # A path that ends a sentence or a log field (".../gate-2." or ".../gate-2:").
                    named |= {value.rstrip('.:') for value in named}
                    values = set()
                    for value in named:
                        # The path named, and the same path read through a linked prefix at any
                        # runs/ or suite/ on the way (the rest stays lexical: it may pass a link
                        # inside the run, such as its runtime).
                        resolved = {os.path.realpath(value)}
                        for marker in re.finditer(r'/(?:runs|suite)/', value):
                            resolved.add(os.path.realpath(value[:marker.end()-1])+value[marker.end()-1:])
                        values.update(path for path in resolved if inside(path, validation))
                    values = sorted(values)
                    entry = [*identity, values]
                files[path] = entry
                found.update(entry[4])
    return {Path(value) for value in found}, files, complete


def ancestors(path):
    path = os.fspath(path)
    while True:
        yield path
        parent = os.path.dirname(path)
        if parent == path:
            return
        path = parent


def gone(error):
    return error.errno in (errno.ENOENT, errno.ESRCH, errno.ENOTDIR)


def children(path):
    try:
        return sorted(path.iterdir())
    except OSError as error:
        if gone(error):
            return []
        raise


def argument_paths(cwd, values):
    """The paths in a process's arguments and environment (`NAME=value`): absolute ones, and
    relative ones from its working directory."""
    paths = []
    for raw in values:
        value = os.fsdecode(raw).split('=', 1)[-1]
        # Colon-separated paths also cover library/search environments.
        for part in value.split(os.pathsep):
            if part.startswith('/'):
                paths.append(Path(part))
            elif part and '/' in part and not part.startswith('-'):
                paths.append(cwd/part)
    return paths


def darwin_process_paths(uid, exclude_pid=None):
    """process_paths on macOS (tools/agents/processes.py): the same paths of every live process
    of the user, failing closed alike."""
    found = set()
    for process in processes.darwin_processes(uid, exclude=(os.getpid(), exclude_pid)):
        cwd = process.cwd or Path('/')
        paths = [cwd, *([process.executable] if process.executable else []), *process.files,
                 *argument_paths(cwd, [*process.arguments, *process.environment])]
        found.update(p.resolve() for p in paths if p.is_absolute())
    return found


def process_paths(proc=Path('/proc'), *, uid=None, privileged=True, exclude_pid=None):
    """Open descriptors, mapped files, cwd, executables and pending commands.

    Other users cannot access private caches. Fail closed for unreadable processes
    of this user, except processes that exited during discovery.
    """
    uid = os.getuid() if uid is None else uid
    if processes.MACOS and proc == Path('/proc'):
        return darwin_process_paths(uid, exclude_pid)
    if not (proc/'self/stat').is_file():
        raise OSError('janitor requires Linux /proc process inspection')
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
            paths.extend(argument_paths(cwd, [*arguments, *variables]))
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


def link_target(path):
    """(lexical target, resolved target, whether it is a directory), or the OSError.
    Read on the I/O threads."""
    try:
        path = Path(path)
        target = Path(os.readlink(path))
        target = target if target.is_absolute() else path.parent/target
        resolved = target.resolve()
        return Path(os.path.abspath(target)), resolved, target.is_dir()
    except OSError as error:
        return error


def current_entries(directory):
    """A directory's links and subdirectories as listed now, outside the index (it is changing);
    None when it is gone."""
    try:
        with os.scandir(directory) as entries:
            return [Entry(directory, entry.name, 'link' if entry.is_symlink() else 'dir') for entry in entries
                    if entry.is_symlink() or entry.is_dir(follow_symlinks=False)]
    except OSError as error:
        if gone(error):
            return None
        raise


def link_walk(roots, *, excluded=(), seconds=300, index_root=None, prune=True):
    """Every link at any depth under roots, following links to directories: ([(link, destination)],
    places still changing after the last recheck), with the lexical and the resolved destination of
    each link (lexical paths protect the intermediate links of a chain). Installations and
    compiler stores are excluded.

    Workers write meanwhile. A directory that vanishes is skipped, and one that changes while it
    is listed is listed directly; after the walk, the nearest listed place above each vanished
    directory and the changed directories are listed again (up to RECHECK_ROUNDS times), so that a
    folder renamed in place (an Editor rotating its cache folders) is walked under its new name. A directory moved
    elsewhere during the walk can be missed (see the module docstring). Raises TimeoutError when
    the walk does not finish in seconds: an incomplete walk permits no removal. The walk visits
    millions of directories, so it keeps paths as strings."""
    deadline = time.monotonic()+seconds
    excluded = {os.fspath(Path(p).resolve()) for p in excluded}
    roots = list(dict.fromkeys(os.fspath(Path(p).absolute()) for p in roots))
    # One shared index of listings for every scan root; no index files in checkouts.
    index = DirectoryIndex(Path(index_root or roots[0]), seconds=seconds)
    found, visited, pending, known, changed = [], {}, list(roots), set(), set()
    # Directories listed so far: only their vanished children are looked for again (a link
    # target outside the roots that vanished leaves no listing of its own place to read again).
    listed_paths = set()

    def vanished(directory):
        """Look again at the nearest listed place above a directory that vanished (one renamed
        after its own listing has vanished with it)."""
        parent = os.path.dirname(directory)
        while parent in listed_paths:
            if os.path.lexists(parent):
                changed.add(parent)
                return
            parent = os.path.dirname(parent)

    def walked(directory, identity):
        """Whether the directory with this identity was walked already (perhaps under another path)."""
        previous = visited.get(identity)
        if previous is None:
            return False
        if previous == directory:
            return True
        try:
            previous_info = os.stat(previous)
        except OSError as error:
            if not gone(error):
                raise
            return False
        return (previous_info.st_dev, previous_info.st_ino) == identity  # reached through a link

    def follow(listed):
        """Record the links among listed entries and queue their directories."""
        links = [entry.path for entries in listed for entry in entries
                 if entry.kind == 'link' and entry.path not in known]
        known.update(links)
        # Reading a link waits for its inode; resolve them in parallel.
        for link, result in zip(links, index.map(link_target, links)):
            if isinstance(result, OSError):
                if gone(result):
                    continue
                raise result
            lexical, target, is_dir = result
            found.append((link, os.fspath(lexical)))
            found.append((link, os.fspath(target)))
            if is_dir and not any(inside(target, path) for path in excluded):
                pending.append(os.fspath(target))
        pending.extend(entry.path for entries in listed for entry in entries
                       if entry.kind == 'dir' and entry.path not in excluded)

    try:
        index.warm_all()
        rounds = 0
        while True:
            while pending:
                if time.monotonic() > deadline:
                    raise TimeoutError(f'link walk exceeded {seconds:.0f}s; no removal in this run')
                batch = pending[-index.batch_size:]
                del pending[-index.batch_size:]
                listed = []
                for directory, info in zip(batch, index.stat_many(batch)):
                    if isinstance(info, OSError):
                        if not gone(info):
                            raise info
                        vanished(directory)
                        continue
                    identity = (info.st_dev, info.st_ino)
                    if walked(directory, identity):
                        continue
                    try:
                        entries = index.entries(directory, info)
                    except DirectoryChanged:
                        entries = current_entries(directory)
                        if entries is None:
                            vanished(directory)
                            continue
                        changed.add(directory)
                    except OSError as error:
                        if not gone(error):
                            raise
                        vanished(directory)
                        continue
                    visited[identity] = directory
                    listed_paths.add(directory)
                    listed.append(entries)
                follow(listed)
            if not changed or rounds == RECHECK_ROUNDS:
                break
            # List the changed places again: what appeared there meanwhile is walked now.
            rounds += 1
            recheck = sorted(changed)
            changed.clear()
            listed = []
            for directory in recheck:
                if any(inside(directory, path) for path in excluded):
                    continue
                entries = current_entries(directory)
                if entries is None:
                    vanished(directory)  # renamed or removed since: look higher up
                else:
                    listed.append(entries)
            follow(listed)
        if prune:
            index.finish()
    finally:
        index.close()
    return found, sorted(changed)


class PathSet:
    """Paths, answering in O(depth) whether one of them lies inside a path (holds) or overlaps it
    (either lies inside the other)."""
    def __init__(self, paths=()):
        self.paths = {os.fspath(path) for path in paths}
        self.holders = {parent for path in self.paths for parent in ancestors(path)}

    def __len__(self):
        return len(self.paths)

    def holds(self, path):
        return os.fspath(path) in self.holders

    def overlaps(self, path):
        return self.holds(path) or any(parent in self.paths for parent in ancestors(path))


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


def numbered(name):
    return re.search(r'\d', name) is not None


def retained_name(name):
    return any(value in name.lower() for value in RETAINED_NAMES)


class Janitor:
    def __init__(self, repo, *, apply=False, idle_hours=DEFAULT_IDLE_HOURS, run_hours=DEFAULT_RUN_HOURS,
                 suite_days=DEFAULT_SUITE_DAYS, log=None, extra_protected=(), scan_seconds=DEFAULT_SCAN_SECONDS,
                 run_seconds=DEFAULT_RUN_SECONDS):
        self.repo = Path(repo).resolve()
        self.cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'
        self.builds = build.build_root()
        self.volume = self.builds.parent
        # Where probes write their runs (tests/probe_paths.py validation_cache): on Linux a link to
        # the build volume's unbound-validation, on macOS a directory of the cache itself.
        probes = self.cache/'validation'
        self.validation = probes.resolve() if probes.is_dir() else self.volume/'unbound-validation'
        self.runtimes = runtime_snapshot.cache_root().resolve()
        self.apply, self.idle_hours, self.run_hours, self.suite_days = apply, idle_hours, run_hours, suite_days
        self.log, self.scan_seconds, self.run_seconds = log, scan_seconds, run_seconds
        self.protected = [Path(p).resolve() for p in extra_protected]
        self.protected_paths = PathSet(self.protected)
        self.removed = self.reclaimable = 0
        # Paths kept, by reason: counted for the summary, not logged one by one.
        self.kept = {}
        self.partial = False
        self.deadline = None
        # The promoted suite's protected paths and roots (see promoted_suite_paths).
        self.suite_paths, self.suite_roots, self.baseline_signature = PathSet(), set(), None
        # Validation paths named by sheriff suites and agent records, and whether all were read.
        self.named, self.named_complete = PathSet(), False
        self.states, self.active, self.queue_signature = {}, set(), None
        self.processes, self.processes_at = PathSet(), None
        # (link, destination) pairs of the last walk, when it started, and per candidate the
        # links outside it that name something inside it.
        self.links, self.links_at, self.incoming = [], None, {}
        self.candidates = {}
        # What this run removed (or would remove, in a dry run): a link inside it protects nothing.
        self.removed_paths = set()

    def emit(self, action, **fields):
        line = json.dumps({'time': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
                           'action': action, **fields}, sort_keys=True)
        print(line, flush=True)
        if self.log:
            self.log.write(line+'\n')
            self.log.flush()

    def budget(self):
        remaining = self.deadline-time.monotonic() if self.deadline is not None else self.scan_seconds
        if remaining <= 0:
            raise TimeoutError('janitor run budget exhausted; remaining work retained')
        return remaining

    def collect(self):
        self.deadline = time.monotonic()+self.run_seconds
        try:
            self.discover()
            self.remove_candidates()
            # The runtime collector holds the snapshot lock that land gates and suites wait for:
            # walk before it, never while it runs.
            if time.monotonic()-self.links_at >= RUNTIME_WALK_SECONDS:
                self.walk_links()
            runtime_snapshot.cleanup(days=self.idle_hours/24, cache=self.runtimes, repo=self.repo,
                                     remove=lambda path: self.remove(path, 'runtime', self.idle_hours, walk=False),
                                     dry_run=not self.apply)
        except TimeoutError as error:
            self.partial = True
            self.emit('stopped', reason=str(error), removals=self.removed)
        # Cache GC has independent publication locks and held-output rules: a partial pass does not
        # starve it.
        self.kache_gc()

    def discover(self):
        self.refresh_queue()
        checkouts = [entry[0] for entry in worktrees(self.repo)]
        main = main_checkout(self.repo)
        slots = slot_root()
        self.protected += [main, slots, slots.parent/'land-trains', self.repo,
                           *checkouts, *(build.default_target(p, self.builds) for p in [main, self.repo, *checkouts]),
                           *(build.default_target(slots/f'slot-{n:02}', self.builds) for n in range(1, 11))]
        self.protected_paths = PathSet(self.protected)
        self.excluded = [self.builds/'kache', self.runtimes/'snapshots', self.cache/'editors',
                         self.cache/'tools', self.volume/'editors', self.volume/'tools',
                         Path.home()/'Unity/Hub/Editor']
        self.refresh_baseline(force=True)
        self.scan_roots = [self.cache, self.volume, *checkouts]
        self.read_named_references()
        self.candidates = {os.fspath(path): (path, kind, hours) for path, kind, hours in self.find_candidates()}
        self.walk_links()
        self.refresh_processes(force=True)

    def read_named_references(self):
        """Validation paths named by any sheriff suite's JSON and logs and by the agents' records
        (promoted and captured baselines, post-land checks). Read once and cached per file; until
        every file has been read, no run or suite label is removed."""
        agents = self.cache/'agents'
        roots = sorted({path for base in (agents/'suite', self.validation/'suite')
                        for path in base.glob('sheriff-*') if path.is_dir()})
        roots += [path for path in (agents/'baseline', agents/'baseline-captures', agents/'postland') if path.is_dir()]
        state = agents/'janitor/suite-references.json'
        try:
            saved = json.loads(state.read_text())
        except FileNotFoundError:
            saved = {}
        known = saved.get('files', {}) if saved.get('format') == SUITE_REFERENCE_FORMAT else {}
        references, files, complete = validation_references(
            roots, self.validation, known, min(SUITE_REFERENCE_SECONDS, self.budget()))
        state.parent.mkdir(parents=True, exist_ok=True)
        temporary = state.with_name(state.name+f'.{os.getpid()}')
        temporary.write_text(json.dumps({'format': SUITE_REFERENCE_FORMAT, 'files': files}))
        os.replace(temporary, state)
        self.named, self.named_complete = PathSet(references), complete
        self.emit('named_references', roots=len(roots), files=len(files), paths=len(references), complete=complete)

    def walk_links(self):
        started = time.monotonic()
        # The promoted suite's metadata is read again with every walk (a promotion at once).
        self.refresh_baseline(force=self.links_at is not None)
        self.links, unsettled = link_walk([*self.scan_roots, *sorted(self.suite_roots)], excluded=self.excluded,
                                          index_root=self.cache, seconds=min(self.scan_seconds, self.budget()))
        self.links_at = started
        # Per candidate, the links outside it that name something inside it.
        self.incoming = {}
        for link, destination in self.links:
            for parent in ancestors(destination):
                if parent in self.candidates and not inside(link, parent):
                    self.incoming.setdefault(parent, set()).add(link)
        for link, destination in self.links:
            if self.within_removed(destination) and not self.within_removed(link):
                self.emit('error', link=link, destination=destination, reason='a link names a directory removed in this run')
        # Places still changing after the last recheck: a link created there may be unseen.
        self.emit('links', links=len(self.links), candidates_named=len(self.incoming),
                  unsettled=len(unsettled), unsettled_examples=unsettled[:5],
                  seconds=round(time.monotonic()-started, 3))

    def refresh_processes(self, force=False):
        if force or self.processes_at is None or time.monotonic()-self.processes_at >= PROCESS_GUARD_SECONDS:
            self.processes = PathSet(process_paths())
            self.processes_at = time.monotonic()

    def refresh_queue(self):
        queue = self.cache/'agents/QUEUE.md'
        try:
            info = queue.stat()
        except FileNotFoundError:
            # A machine without the orchestrator's queue (a Mac whose queue is on the Linux
            # machine): no task is known to have landed, so every task cache stays; runs keep
            # their age, link and live-process guards.
            if self.queue_signature != 'missing':
                self.emit('queue_missing', path=str(queue))
            self.states, self.active, self.queue_signature = {}, set(), 'missing'
            return
        signature = (info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns, info.st_size)
        if signature != self.queue_signature:
            self.states = task_states(queue)
            self.active = active_tasks(queue)
            self.queue_signature = signature

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
            self.suite_paths = PathSet(self.suite_paths.paths | {os.fspath(p) for p in protected})
            self.suite_roots.update(roots)
            self.baseline_signature = signature

    def find_candidates(self):
        yield from ((path, 'run', self.run_hours) for path in self.run_instances())
        yield from ((path, 'suite', self.suite_days*24) for path in self.suite_labels())
        yield from self.other_candidates()

    def run_instances(self):
        """Validation run instances under runs/: a dated folder is one run; in another (a probe's)
        folder and in its runs/ folder, the numbered folders are its runs, except the newest of
        each (a probe can reuse its last run). Folders without numbers there (logs, tools, a single
        run's capture and project) and the files beside them stay, as do sheriff and baseline runs."""
        root = self.validation/'runs'
        if not root.is_dir():
            return
        for path in children(root):
            if not path.is_dir() or path.is_symlink():
                continue
            if retained_name(path.name):
                continue
            if re.search(r'(?<!\d)20\d{6}(?!\d)', path.name) or re.search(r'-t3-[0-9a-f]+', path.name):
                yield path
                continue
            for parent in (path, path/'runs'):
                if parent.is_symlink():
                    continue  # its runs lie elsewhere, under other names
                instances = []
                for child in children(parent):
                    try:
                        info = child.lstat()
                    except FileNotFoundError:
                        continue
                    if stat.S_ISDIR(info.st_mode) and numbered(child.name):
                        instances.append((info.st_mtime, child))
                if not instances:
                    continue
                latest = max(instances)[1]
                self.keep(latest, 'newest run of its probe')
                for _, instance in sorted(instances):
                    if instance != latest and not retained_name(instance.name):
                        yield instance

    def suite_labels(self):
        """Suite label folders under suite/, except sheriff and baseline ones."""
        root = self.validation/'suite'
        if root.is_dir():
            for path in children(root):
                if path.is_dir() and not path.is_symlink() and not retained_name(path.name):
                    yield path

    def other_candidates(self):
        for root in dict.fromkeys([self.volume, self.cache.resolve()]):
            for path in children(root):
                if not re.fullmatch(r'q\d+', path.name, re.I) or path.is_symlink() or not path.is_dir():
                    continue
                if self.states.get(path.name.lower()) != 'landed':
                    self.keep(path, 'task is not landed')
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

    def named_protection(self, path):
        """Protections that do not depend on links or processes."""
        if self.suite_paths.overlaps(path):
            return 'promoted sheriff suite reference'
        if self.named.overlaps(path):
            return 'named by a sheriff suite or an agent record'
        if any(value.lower() in self.active for part in Path(path).parts
               for value in re.findall(r'(?<![a-zA-Z0-9])q\d+(?!\d)', part, re.I)):
            return 'active task directory'
        if self.protected_paths.overlaps(path):
            return 'protected checkout, slot or train'
        return None

    def within_removed(self, path):
        return any(parent in self.removed_paths for parent in ancestors(path))

    def owner(self, path):
        """The candidate that holds path, if any."""
        return next((parent for parent in ancestors(path) if parent in self.candidates), None)

    def link_protection(self, path):
        """A link outside path that names something inside it, unless that link lay in a directory
        removed in this run."""
        key = os.fspath(path)
        sources = self.incoming.get(key) if key in self.candidates else [
            link for link, destination in self.links if inside(destination, key) and not inside(link, key)]
        return next((link for link in sources or () if not self.within_removed(link)), None)

    def protection(self, path):
        if reason := self.named_protection(path):
            return reason
        if self.processes.holds(path):
            return 'live process'
        if link := self.link_protection(path):
            return f'symlink reference: {link}'
        return None

    def check(self, path, hours, directory_ctimes=None):
        """Why path stays, or None; with its allocated and reclaimable bytes."""
        if reason := self.protection(path):
            return reason, 0, 0
        try:
            newest, allocated, reclaimable = inventory(path, directory_ctimes)
        except OSError as error:
            if not gone(error):
                raise
            # Something inside went while it was looked at: it is being written to.
            return ('gone' if not os.path.lexists(path) else 'modified during the check'), 0, 0
        if newest >= time.time()-hours*3600:
            return 'recently modified', 0, 0
        return None, allocated, reclaimable

    def keep(self, path, reason):
        """Counts a kept path under its reason (a symlink reference: under 'symlink reference')."""
        if reason != 'gone':
            key = reason.split(':', 1)[0]
            self.kept[key] = self.kept.get(key, 0)+1

    def remove_candidates(self):
        """Each candidate is checked and removed in turn. A link from another candidate protects
        until that one is removed, so the candidates that hold such links go first."""
        keys = list(self.candidates)
        if not self.named_complete:
            unread = [key for key in keys if self.candidates[key][1] in ('run', 'suite')]
            if unread:
                self.partial = True
                self.emit('keep', runs=len(unread), reason='sheriff suite and agent references not all read yet')
                keys = [key for key in keys if self.candidates[key][1] not in ('run', 'suite')]
        holders = {self.owner(link) for links in self.incoming.values() for link in links}
        for key in sorted(keys, key=lambda key: key not in holders):
            path, kind, hours = self.candidates[key]
            if kind == 'target':
                self.remove_target(path, hours)
            else:
                self.remove(path, kind, hours)

    def remove_target(self, path, hours):
        marker = path/'.unbound-source-root'
        if not marker.is_file() or marker.is_symlink():
            return self.keep(path, 'no trustworthy checkout marker')
        try:
            with marker.open() as stream:
                try:
                    fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    return self.keep(path, 'target is claimed')
                owner = stream.read().strip()
                if not owner or not Path(owner).is_absolute() or Path(owner).exists():
                    return self.keep(path, 'checkout exists or marker is invalid')
                self.remove(path, 'target', hours)
        except FileNotFoundError:
            return

    def remove(self, path, kind, hours, walk=True):
        """Check path again with fresh guards and remove it; True when it was removed (or would be,
        in a dry run). Every removal goes through here, the runtime collector's too (walk=False: a
        stale walk is not repeated there, see collect)."""
        path = Path(os.path.abspath(path))
        self.budget()
        if self.links_at is None:
            self.discover()  # nothing is removed without a walk
        elif walk and time.monotonic()-self.links_at >= LINK_SNAPSHOT_SECONDS:
            self.walk_links()
        self.refresh_processes()
        self.refresh_queue()
        self.refresh_baseline()
        if kind == 'task':
            task = next((p.name.lower() for p in path.parents
                         if p.parent in (self.volume, self.cache.resolve()) and re.fullmatch(r'q\d+', p.name, re.I)), '')
            if self.states.get(task) != 'landed':
                self.keep(path, 'task state changed')
                return False
        reason, allocated, reclaimable = self.check(path, hours)
        directory = path.is_dir() and not path.is_symlink()
        if not reason and self.apply and directory:
            directory_ctimes = {}
            try:
                # Only directories need chmod; files can share live target inodes.
                for _, _, _, descriptor in os.fwalk(path, follow_symlinks=False):
                    before = os.fstat(descriptor)
                    mode = stat.S_IMODE(before.st_mode)
                    if mode & stat.S_IRWXU != stat.S_IRWXU:
                        os.fchmod(descriptor, mode | stat.S_IRWXU)
                        after = os.fstat(descriptor)
                        directory_ctimes[(before.st_dev, before.st_ino)] = (after.st_ctime_ns, before.st_ctime)
            except OSError as error:
                if not gone(error):
                    raise
                reason = 'modified during the check'
            if not reason and directory_ctimes:
                # Recheck every descendant: only the exact ctime changes made above are excluded
                # from the age test.
                reason, allocated, reclaimable = self.check(path, hours, directory_ctimes)
        if reason:
            self.keep(path, reason)
            return False
        if self.apply:
            self.emit('removing', path=str(path), kind=kind, allocated_bytes=allocated,
                      reclaimable_bytes=reclaimable)
            try:
                if directory:
                    shutil.rmtree(path)
                else:
                    path.unlink()
            except FileNotFoundError:
                if os.path.lexists(path):
                    # Something inside went during the removal: what is left stays, and its links
                    # protect as before.
                    self.keep(path, 'modified during the removal')
                    return False
        self.removed_paths.add(os.fspath(path))
        self.emit('removed' if self.apply else 'would_remove', path=str(path), kind=kind,
                  allocated_bytes=allocated, reclaimable_bytes=reclaimable)
        self.removed += 1
        self.reclaimable += reclaimable
        return True

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
    parser.add_argument('--run-hours', type=float, default=DEFAULT_RUN_HOURS,
                        help='age of the newest file before a validation run instance goes')
    parser.add_argument('--suite-days', type=float, default=DEFAULT_SUITE_DAYS,
                        help='age of the newest file before a suite label folder goes')
    parser.add_argument('--scan-seconds', type=float, default=DEFAULT_SCAN_SECONDS)
    parser.add_argument('--run-seconds', type=float, default=DEFAULT_RUN_SECONDS)
    parser.add_argument('--protect', type=Path, action='append', default=[])
    args = parser.parse_args(argv)
    if args.process_paths_only is not None:
        print(json.dumps(sorted(str(p) for p in process_paths(uid=args.process_paths_only, privileged=False, exclude_pid=args.exclude_process))))
        return 0
    if args.apply and args.dry_run or min(args.idle_hours, args.run_hours, args.suite_days, args.scan_seconds, args.run_seconds) <= 0:
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
            janitor = Janitor(args.repo, apply=args.apply, idle_hours=args.idle_hours, run_hours=args.run_hours,
                              suite_days=args.suite_days,
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
            janitor.emit('summary', apply=args.apply, failed=failed, partial=janitor.partial,
                          removals=janitor.removed, kept=janitor.kept,
                          reclaimable_bytes=janitor.reclaimable, free_bytes=after,
                          free_change_bytes={p: after[p]-value for p, value in before.items() if p in after})
    return 1 if failed else 3 if janitor.partial else 0


if __name__ == '__main__':
    raise SystemExit(main())
