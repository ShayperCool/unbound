"""Cached listings retain deep links and detect edits without rescanning file histories."""
import os
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
