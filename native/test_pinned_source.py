"""Tests of native/pinned_source.py: python3 -m unittest discover -s native -p 'test_pinned_source.py'"""
import hashlib
import io
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest import mock

import pinned_source


def archive(path, members):
    """A gzip tar with (name, bytes | ('link', target)) members."""
    with tarfile.open(path, 'w:gz') as tar:
        for name, data in members:
            info = tarfile.TarInfo(name)
            if isinstance(data, tuple):
                info.type = tarfile.SYMTYPE
                info.linkname = data[1]
                tar.addfile(info)
            else:
                info.size = len(data)
                tar.addfile(info, io.BytesIO(data))
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class PinnedSourceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        root = Path(self.directory.name)
        self.cache = root / 'cache'
        self.script = root / 'source.py'
        self.script.write_text('# preparation script\n')
        self.archive = root / 'source.tar.gz'
        environment = {'XDG_CACHE_HOME': str(self.cache), 'TEST_ARCHIVE': str(self.archive)}
        patcher = mock.patch.dict(os.environ, environment)
        patcher.start()
        self.addCleanup(patcher.stop)
        self.addCleanup(self.directory.cleanup)

    def prepare(self, sha256, adjust=lambda root: None):
        return pinned_source.prepare(
            name='test', label='1', url='https://example.invalid/source.tar.gz', sha256=sha256,
            archive_name='source.tar.gz', archive_variable='TEST_ARCHIVE', limit=1 << 20,
            top='top', select=lambda relative: relative.parts[0] == 'keep', adjust=adjust,
            inputs=[self.script])

    def test_extracts_selected_files_and_adjusts_once(self):
        sha = archive(self.archive, [('top/keep/a.c', b'a'), ('top/skip/b.c', b'b'), ('top/README', b'r')])
        calls = []
        def adjust(root):
            calls.append(root)
            (root / 'keep/generated.h').write_text('g')
        first = self.prepare(sha, adjust)
        self.assertEqual((first / 'keep/a.c').read_bytes(), b'a')
        self.assertEqual((first / 'keep/generated.h').read_text(), 'g')
        self.assertFalse((first / 'skip').exists())
        self.assertFalse((first / 'README').exists())
        self.assertEqual(self.prepare(sha, adjust), first)
        self.assertEqual(len(calls), 1)

    def test_a_changed_script_gets_a_new_directory(self):
        sha = archive(self.archive, [('top/keep/a.c', b'a')])
        first = self.prepare(sha)
        self.script.write_text('# changed preparation\n')
        self.assertNotEqual(self.prepare(sha), first)

    def test_refuses_a_checksum_mismatch(self):
        archive(self.archive, [('top/keep/a.c', b'a')])
        with self.assertRaisesRegex(RuntimeError, 'checksum mismatch'):
            self.prepare('0' * 64)

    def test_refuses_path_traversal_and_other_top_directories(self):
        for name in ('top/../escape.c', 'other/keep/a.c'):
            sha = archive(self.archive, [(name, b'x')])
            with self.assertRaisesRegex(RuntimeError, 'Invalid test archive path'):
                self.prepare(sha)

    def test_skips_links(self):
        sha = archive(self.archive, [('top/keep/a.c', b'a'), ('top/keep/link.c', ('link', '/etc/passwd'))])
        prepared = self.prepare(sha)
        self.assertFalse((prepared / 'keep/link.c').exists())

    def test_a_failed_adjustment_publishes_nothing(self):
        sha = archive(self.archive, [('top/keep/a.c', b'a')])
        def fail(root):
            raise RuntimeError('pinned source does not match')
        with self.assertRaisesRegex(RuntimeError, 'does not match'):
            self.prepare(sha, fail)
        self.assertEqual([p.name for p in (self.cache / 'unbound/dependencies/test').iterdir()], ['prepare.lock'])

    def test_offline_without_an_archive_fails_before_downloading(self):
        os.environ.pop('TEST_ARCHIVE')
        # Any value of UNBOUND_OFFLINE forbids downloading, as for the engine.
        for value in ('1', '0', ''):
            with mock.patch.dict(os.environ, {'UNBOUND_OFFLINE': value}), \
                    mock.patch.object(pinned_source, 'download') as download:
                with self.assertRaisesRegex(RuntimeError, 'archive is unavailable'):
                    self.prepare('0' * 64)
                download.assert_not_called()

    def test_an_empty_cache_home_means_the_default(self):
        with mock.patch.dict(os.environ, {'XDG_CACHE_HOME': ''}):
            self.assertEqual(pinned_source.cache_root('test'), Path.home() / '.cache/unbound/dependencies/test')


if __name__ == '__main__':
    unittest.main()
