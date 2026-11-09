"""Incremental directory listings for the conservative run-cache link walk."""
import hashlib
import concurrent.futures
import json
import os
from pathlib import Path
import sqlite3
import time


class Entry:
    def __init__(self, directory, name, kind):
        self.name, self.kind = name, kind
        self.path = str(directory/name)

    def is_symlink(self):
        return self.kind == 'link'

    def is_dir(self, follow_symlinks=False):
        return self.kind == 'dir'

    def stat(self):
        return os.stat(self.path)


class DirectoryIndex:
    def __init__(self, base, *, seconds=120):
        key = hashlib.sha256(os.fsencode(base)).hexdigest()
        path = base.parent/'.unbound-link-index'/key
        path.parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(path, timeout=1)
        self.db.execute('PRAGMA journal_mode=WAL')
        self.db.execute('CREATE TABLE IF NOT EXISTS directories (path TEXT PRIMARY KEY, stamp TEXT, entries TEXT)')
        self.deadline = time.monotonic()+seconds
        self.seconds = seconds
        self.cached = {}
        self.reused = self.scanned = 0
        self.seen = set()
        self.pool = concurrent.futures.ThreadPoolExecutor(max_workers=16)
        self.prepared = {}

    def stat_many(self, paths):
        keys = tuple(dict.fromkeys(map(str, paths)))
        self.cached = {path: None for path in keys}
        if keys:
            self.cached.update((path, (stamp, values)) for path, stamp, values in self.db.execute(
                'SELECT path, stamp, entries FROM directories WHERE path IN ('+','.join('?' for _ in keys)+')', keys))
        def inspect(path):
            try:
                return path.stat()
            except OSError as error:
                return error
        infos = list(self.pool.map(inspect, paths))
        tasks = {}
        for path, info in zip(paths, infos):
            if isinstance(info, OSError):
                continue
            row = self.cached.get(str(path))
            stamp = self.stamp(info)
            if row is None or row[0] != stamp:
                tasks[str(path)] = (path, stamp)
        def listing(item):
            path, stamp = item
            try:
                return self.read(path, stamp), None
            except OSError as error:
                return None, error
        self.prepared = dict(zip(tasks, self.pool.map(listing, tasks.values())))
        return infos

    @staticmethod
    def stamp(info):
        return json.dumps([info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns, info.st_mode])

    def read(self, path, stamp):
        with os.scandir(path) as entries:
            values = [(entry.name, 'link' if entry.is_symlink() else 'dir') for entry in entries
                      if entry.is_symlink() or entry.is_dir(follow_symlinks=False)]
        if self.stamp(path.stat()) != stamp:
            raise OSError(f'run-cache directory changed during indexing: {path}; retry reap')
        return values

    def entries(self, path, info):
        if time.monotonic() > self.deadline:
            raise TimeoutError(f'run-cache index exceeded {self.seconds}s; progress saved, retry reap')
        stamp = self.stamp(info)
        self.seen.add(hashlib.sha256(os.fsencode(path)).digest())
        if str(path) in self.cached:
            row = self.cached[str(path)]
        else:
            row = self.db.execute('SELECT stamp, entries FROM directories WHERE path=?', (str(path),)).fetchone()
        if row and row[0] == stamp:
            values = json.loads(row[1])
            self.reused += 1
        else:
            if str(path) in self.prepared:
                values, error = self.prepared.pop(str(path))
                if error:
                    raise error
            else:
                values = self.read(path, stamp)
            self.db.execute('INSERT OR REPLACE INTO directories VALUES (?,?,?)',
                            (str(path), stamp, json.dumps(values)))
            self.scanned += 1
        return [Entry(path, name, kind) for name, kind in values]

    def finish(self):
        stale = []
        for (path,) in self.db.execute('SELECT path FROM directories'):
            if time.monotonic() > self.deadline:
                return  # Pruning stale index rows can wait; discovery is complete.
            if hashlib.sha256(os.fsencode(path)).digest() not in self.seen:
                stale.append((path,))
        self.db.executemany('DELETE FROM directories WHERE path=?', stale)

    def close(self):
        self.pool.shutdown()
        self.db.commit()
        self.db.close()
