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
        self.cached = {path: (stamp, values) for path, stamp, values in
                       self.db.execute('SELECT path, stamp, entries FROM directories')}
        self.reused = self.scanned = 0
        self.seen = set()
        self.pool = concurrent.futures.ThreadPoolExecutor(max_workers=16)

    def stat_many(self, paths):
        def inspect(path):
            try:
                return path.stat()
            except OSError as error:
                return error
        return list(self.pool.map(inspect, paths))

    @staticmethod
    def stamp(info):
        return json.dumps([info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns, info.st_mode])

    def entries(self, path, info):
        if time.monotonic() > self.deadline:
            raise TimeoutError(f'run-cache index exceeded {self.seconds}s; progress saved, retry reap')
        stamp = self.stamp(info)
        self.seen.add(str(path))
        row = self.cached.get(str(path))
        if row and row[0] == stamp:
            values = json.loads(row[1])
            self.reused += 1
        else:
            with os.scandir(path) as entries:
                values = [(entry.name, 'link' if entry.is_symlink() else 'dir') for entry in entries
                          if entry.is_symlink() or entry.is_dir(follow_symlinks=False)]
            if self.stamp(path.stat()) != stamp:
                raise OSError(f'run-cache directory changed during indexing: {path}; retry reap')
            self.db.execute('INSERT OR REPLACE INTO directories VALUES (?,?,?)',
                            (str(path), stamp, json.dumps(values)))
            self.scanned += 1
        return [Entry(path, name, kind) for name, kind in values]

    def finish(self):
        self.db.executemany('DELETE FROM directories WHERE path=?',
                            ((path,) for path in self.cached.keys()-self.seen))

    def close(self):
        self.pool.shutdown()
        self.db.commit()
        self.db.close()
