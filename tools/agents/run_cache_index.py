"""Incremental directory listings for the conservative run-cache link walk."""
import hashlib
import concurrent.futures
import json
import os
from pathlib import Path
import sqlite3
import sys
import time


class Entry:
    __slots__ = ('name', 'kind', 'path')

    def __init__(self, directory, name, kind):
        self.name, self.kind = name, kind
        self.path = os.path.join(directory, name)

    def is_symlink(self):
        return self.kind == 'link'

    def is_dir(self, follow_symlinks=False):
        return self.kind == 'dir'

    def stat(self):
        return os.stat(self.path)


class DirectoryIndex:
    # Bounded batches amortize executor and SQL overhead across directory checks.
    batch_size = 512
    io_chunk = 32
    io_workers = 16
    progress_seconds = 10

    # The index of a large cache holds millions of rows; reading it through a memory
    # map instead of read() calls keeps per-directory lookups cheap.
    mmap_bytes = 8*2**30

    def __init__(self, base, *, seconds=300):
        key = hashlib.sha256(os.fsencode(base)).hexdigest()
        path = base.parent/'.unbound-link-index'/key
        path.parent.mkdir(parents=True, exist_ok=True)
        self.deadline = None if seconds is None else time.monotonic()+seconds
        self.seconds = seconds
        self.db = sqlite3.connect(path, timeout=10)
        self.execute('PRAGMA journal_mode=WAL')
        self.execute(f'PRAGMA mmap_size={self.mmap_bytes}')
        self.execute('CREATE TABLE IF NOT EXISTS directories (path TEXT PRIMARY KEY, stamp TEXT, entries TEXT)')
        self.next_progress = time.monotonic()+self.progress_seconds
        self.cached = {}
        self.reused = self.scanned = 0
        self.seen = set()
        self.pool = concurrent.futures.ThreadPoolExecutor(max_workers=self.io_workers)
        self.prepared = {}

    def execute(self, statement, parameters=()):
        # Other cache walkers may still be running. A busy index is not evidence
        # that a checkout is unreferenced; wait without weakening discovery.
        while True:
            try:
                return self.db.execute(statement, parameters)
            except sqlite3.OperationalError as error:
                if getattr(error, 'sqlite_errorcode', 0) & 0xff not in (sqlite3.SQLITE_BUSY, sqlite3.SQLITE_LOCKED):
                    raise
                if self.deadline is not None and time.monotonic() > self.deadline:
                    raise TimeoutError(f'run-cache index exceeded {self.seconds}s while waiting for database writer') from error
                time.sleep(0.01)  # SQLITE_LOCKED can return before the SQLite busy timeout.
                print('run-cache index: waiting for database writer', file=sys.stderr, flush=True)

    def map(self, function, items):
        """function over items on the I/O threads, in order."""
        groups = [items[start:start+self.io_chunk] for start in range(0, len(items), self.io_chunk)]
        return [value for group in self.pool.map(lambda group: [function(item) for item in group], groups)
                for value in group]

    def warm(self, paths):
        """Read the directories' inodes in parallel, so that serial per-directory work after
        it (a watch, a stat) finds them cached instead of waiting for the disk one by one."""
        def touch(group):
            for path in group:
                try:
                    os.stat(path)
                except OSError:
                    pass
        groups = [paths[start:start+self.io_chunk] for start in range(0, len(paths), self.io_chunk)]
        for _ in self.pool.map(touch, groups):
            pass

    def stat_many(self, paths):
        # Publish each completed batch, even when another walker is still indexing.
        # Never hold the writer lock across the next batch's filesystem I/O.
        self.db.commit()
        if time.monotonic() >= self.next_progress:
            print(f'run-cache index: {self.reused} directories reused, {self.scanned} listed',
                  file=sys.stderr, flush=True)
            self.next_progress = time.monotonic()+self.progress_seconds
        keys = tuple(dict.fromkeys(map(os.fspath, paths)))
        self.cached = {path: None for path in keys}
        if keys:
            self.cached.update((path, (stamp, values)) for path, stamp, values in self.execute(
                'SELECT path, stamp, entries FROM directories WHERE path IN ('+','.join('?' for _ in keys)+')', keys))
        def inspect(path):
            try:
                return os.stat(path)
            except OSError as error:
                return error
        def inspect_group(group):
            return [inspect(path) for path in group]
        groups = [paths[start:start+self.io_chunk] for start in range(0, len(paths), self.io_chunk)]
        infos = [info for group in self.pool.map(inspect_group, groups) for info in group]
        tasks = {}
        for path, info in zip(paths, infos):
            if isinstance(info, OSError):
                continue
            key = os.fspath(path)
            row = self.cached.get(key)
            stamp = self.stamp(info)
            if row is None or row[0] != stamp:
                tasks[key] = (path, stamp)
        def listing(item):
            path, stamp = item
            try:
                return self.read(path, stamp), None
            except OSError as error:
                return None, error
        def listing_group(group):
            return [listing(item) for item in group]
        items = list(tasks.values())
        groups = [items[start:start+self.io_chunk] for start in range(0, len(items), self.io_chunk)]
        values = [value for group in self.pool.map(listing_group, groups) for value in group]
        self.prepared = dict(zip(tasks, values))
        return infos

    @staticmethod
    def stamp(info):
        return json.dumps([info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns, info.st_mode])

    def read(self, path, stamp):
        with os.scandir(path) as entries:
            values = [(entry.name, 'link' if entry.is_symlink() else 'dir') for entry in entries
                      if entry.is_symlink() or entry.is_dir(follow_symlinks=False)]
        if self.stamp(os.stat(path)) != stamp:
            raise OSError(f'run-cache directory changed during indexing: {path}; retry reap')
        return values

    def entries(self, path, info):
        if self.deadline is not None and time.monotonic() > self.deadline:
            raise TimeoutError(f'run-cache index exceeded {self.seconds}s; listings saved; reap needs a complete validation')
        stamp = self.stamp(info)
        key = os.fspath(path)
        self.seen.add(hashlib.sha256(os.fsencode(key)).digest())
        if key in self.cached:
            row = self.cached[key]
        else:
            row = self.execute('SELECT stamp, entries FROM directories WHERE path=?', (key,)).fetchone()
        if row and row[0] == stamp:
            values = json.loads(row[1])
            self.reused += 1
        else:
            if key in self.prepared:
                values, error = self.prepared.pop(key)
                if error:
                    raise error
            else:
                values = self.read(path, stamp)
            self.execute('INSERT OR REPLACE INTO directories VALUES (?,?,?)',
                            (key, stamp, json.dumps(values)))
            self.scanned += 1
        return [Entry(key, name, kind) for name, kind in values]

    def finish(self):
        stale = []
        for (path,) in self.execute('SELECT path FROM directories'):
            if self.deadline is not None and time.monotonic() > self.deadline:
                return  # Pruning stale index rows can wait; discovery is complete.
            if hashlib.sha256(os.fsencode(path)).digest() not in self.seen:
                stale.append((path,))
        self.db.commit()
        for start in range(0, len(stale), self.batch_size):
            paths = [row[0] for row in stale[start:start+self.batch_size]]
            self.execute('DELETE FROM directories WHERE path IN ('+','.join('?' for _ in paths)+')', paths)
            self.db.commit()

    def close(self):
        self.pool.shutdown()
        self.db.commit()
        self.db.close()
