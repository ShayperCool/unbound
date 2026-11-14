"""Cached listings retain deep links and detect edits without rescanning file histories."""
import os
import sqlite3
import threading
from pathlib import Path
import tempfile
import unittest
from unittest import mock

from run_cache_index import DirectoryIndex
import slot


class IndexTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.base = self.root/'unbound'
        self.deep = self.base/'runs/old/project'
        self.deep.mkdir(parents=True)
        self.target = self.root/'checkout'
        self.target.mkdir()

    def test_warm_walk_never_lists_unchanged_directories_but_detects_deep_new_links(self):
        for number in range(1000):
            (self.deep/str(number)).write_text('history')
        self.assertNotIn(self.target, slot.symlink_targets(self.base))
        with mock.patch('run_cache_index.os.scandir', side_effect=AssertionError('rescanned history')):
            self.assertNotIn(self.target, slot.symlink_targets(self.base))
        before = self.deep.stat()
        (self.deep/'Assets').symlink_to(self.target)
        os.utime(self.deep, ns=(before.st_atime_ns, before.st_mtime_ns))
        self.assertIn(self.target, slot.symlink_targets(self.base))
        (self.deep/'Assets').unlink()
        self.assertNotIn(self.target, slot.symlink_targets(self.base))

    def test_alias_target_changes_and_file_to_directory_changes_invalidate_cache(self):
        other = self.root/'other'
        other.mkdir()
        (self.deep/'alias').symlink_to(other)
        slot.symlink_targets(self.base)
        (other/'link').symlink_to(self.target)
        self.assertIn(self.target, slot.symlink_targets(self.base))
        (other/'link').unlink()
        (self.deep/'new').write_text('file')
        slot.symlink_targets(self.base)
        (self.deep/'new').unlink()
        (self.deep/'new').mkdir()
        (self.deep/'new/link').symlink_to(self.target)
        self.assertIn(self.target, slot.symlink_targets(self.base))

    def test_timeout_saves_progress_and_fails_closed(self):
        index = DirectoryIndex(self.base, seconds=0)
        try:
            with self.assertRaises(TimeoutError):
                index.entries(self.base, self.base.stat())
        finally:
            index.close()
        self.assertEqual(slot.symlink_targets(self.base), set())

    def test_reap_validates_past_the_old_limit_but_bounded_warming_stays_closed(self):
        (self.deep/'Assets').symlink_to(self.target)
        # Simulate validation taking over five minutes, including with warm listings.
        slot.symlink_targets(self.base)
        for seconds in (None, 300):
            ticks = iter(range(0, 10000, 301))
            with mock.patch('run_cache_index.time.monotonic', side_effect=lambda: next(ticks)):
                if seconds is None:
                    self.assertIn(self.target, slot.symlink_targets(self.base))
                else:
                    with self.assertRaisesRegex(SystemExit, 'refusing to reap'):
                        slot.symlink_targets(self.base, seconds=seconds)

    def test_next_batch_publishes_progress_and_releases_the_writer(self):
        first = DirectoryIndex(self.base)
        second = DirectoryIndex(self.base)
        try:
            first.entries(self.base, self.base.stat())
            first.stat_many([self.deep])
            self.assertIsNotNone(second.execute('SELECT path FROM directories WHERE path=?',
                                               (str(self.base),)).fetchone())
            second.db.execute('PRAGMA busy_timeout=20')
            second.entries(self.deep, self.deep.stat())
        finally:
            first.close()
            second.close()

    def test_busy_writer_is_waited_out(self):
        index = DirectoryIndex(self.base)
        writer = sqlite3.connect(index.db.execute('PRAGMA database_list').fetchone()[2],
                                 check_same_thread=False)
        try:
            writer.execute('BEGIN IMMEDIATE')
            index.db.execute('PRAGMA busy_timeout=20')
            release = threading.Timer(0.1, writer.commit)
            release.start()
            try:
                index.entries(self.base, self.base.stat())
            finally:
                release.join()
            self.assertEqual(index.scanned, 1)
        finally:
            writer.close()
            index.close()

    def test_checkout_membership_preserves_nested_roots_and_path_boundaries(self):
        parent, nested, unrelated = [self.root/name for name in ('a', 'a/child', 'a-extra')]
        roots = [parent, nested, unrelated]
        self.assertEqual(slot.referenced_checkouts({nested/'Assets/file'}, roots), {parent, nested})
        self.assertEqual(slot.referenced_checkouts({unrelated}, roots), {unrelated})

    def test_root_spelling_follows_the_host_path_resolution(self):
        (self.deep/'link').symlink_to('/'+str(self.target))
        self.assertIn(self.target.resolve(), slot.symlink_targets(self.base))

    @unittest.skipIf(os.geteuid() == 0, 'root bypasses directory permissions')
    def test_cached_directory_becoming_unreadable_fails_closed(self):
        slot.symlink_targets(self.base)
        self.deep.chmod(0)
        try:
            with self.assertRaisesRegex(SystemExit, 'refusing to reap'):
                slot.symlink_targets(self.base)
        finally:
            self.deep.chmod(0o700)


if __name__ == '__main__':
    unittest.main()
