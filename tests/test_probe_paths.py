"""Checks where probes read and write their references on each platform.

    python3 tests/test_probe_paths.py
"""
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import probe_paths  # noqa: E402


class DeviceReferenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.probe = Path(self.temp.name)/'probe'
        self.root = self.probe/'Reference/unity-R'
        (self.root/'metal').mkdir(parents=True)
        for name in ('unity-R.jsonl', 'unity-R.png', 'unity-R/metal.png', 'unity-R/a.png', 'unity-R/metal/a.png',
                     'unity-R/b.png'):
            (self.probe/'Reference'/name).write_text('x')
        environment = patch.dict('os.environ', {'UNBOUND_PROBE_UNITY_VERSION': 'R'})
        environment.start()
        self.addCleanup(environment.stop)

    def read(self, path, macos=True, publishing=False):
        with patch.object(probe_paths, 'MACOS_ARM64', macos):
            return probe_paths.device_reference(path, self.probe, publishing)

    def test_macos_reads_the_metal_reference_where_it_exists(self):
        self.assertEqual(self.read(self.probe/'Reference/unity-R.png'), self.root/'metal.png')
        self.assertEqual(self.read(self.root/'a.png'), self.root/'metal/a.png')
        # No Metal counterpart yet: the Linux reference, as before.
        self.assertEqual(self.read(self.probe/'Reference/unity-R.jsonl'), self.probe/'Reference/unity-R.jsonl')
        self.assertEqual(self.read(self.root/'b.png'), self.root/'b.png')

    def test_macos_publishes_to_the_metal_reference(self):
        self.assertEqual(self.read(self.probe/'Reference/unity-R.jsonl', publishing=True), self.root/'metal.jsonl')
        self.assertEqual(self.read(self.root/'b.png', publishing=True), self.root/'metal/b.png')
        self.assertEqual(self.read(self.root/'metal/a.png', publishing=True), self.root/'metal/a.png')

    def test_linux_and_other_paths_are_kept(self):
        for path in (self.probe/'Reference/unity-R.png', self.root/'a.png'):
            self.assertEqual(self.read(path, macos=False), path)
            self.assertEqual(self.read(path, macos=False, publishing=True), path)
        for path in (self.probe/'Reference', self.probe/'Reference/unity-S.png', self.probe/'residuals.json'):
            self.assertEqual(self.read(path, publishing=True), path)

    def test_a_flat_reference_layout(self):
        # A probe whose Reference holds its files directly (no unity-R directory).
        flat = Path(self.temp.name)/'flat'
        (flat/'Reference/metal').mkdir(parents=True)
        for name in ('a.png', 'metal/a.png', 'b.png'):
            (flat/'Reference'/name).write_text('x')
        def read(path, macos=True, publishing=False):
            with patch.object(probe_paths, 'MACOS_ARM64', macos):
                return probe_paths.device_reference(path, flat, publishing)
        self.assertEqual(read(flat/'Reference'), flat/'Reference/metal')
        self.assertEqual(read(flat/'Reference/a.png'), flat/'Reference/metal/a.png')
        self.assertEqual(read(flat/'Reference/b.png'), flat/'Reference/b.png')
        self.assertEqual(read(flat/'Reference/b.png', publishing=True), flat/'Reference/metal/b.png')
        self.assertEqual(read(flat/'Reference/metal/a.png'), flat/'Reference/metal/a.png')
        self.assertEqual(read(flat/'Reference/a.png', macos=False), flat/'Reference/a.png')
        # A probe with release directories is not flat: its Reference stays as it is.
        self.assertEqual(self.read(self.probe/'Reference'), self.probe/'Reference')

    def test_a_whole_reference_set(self):
        # The release's directory as one reference (a directory compare) is its Metal directory.
        self.assertEqual(self.read(self.root), self.root/'metal')
        self.assertEqual(self.read(self.root, macos=False), self.root)
        with patch.object(probe_paths, 'MACOS_ARM64', True):
            self.assertEqual(probe_paths.device_reference_root(self.probe), self.root/'metal')
            (self.root/'metal/a.png').unlink()
            (self.root/'metal.png').unlink()
            (self.root/'metal').rmdir()
            self.assertEqual(probe_paths.device_reference_root(self.probe), self.root)
            self.assertEqual(probe_paths.device_reference_root(self.probe, publishing=True), self.root/'metal')
        with patch.object(probe_paths, 'MACOS_ARM64', False):
            self.assertEqual(probe_paths.device_reference_root(self.probe, publishing=True), self.root)


if __name__ == '__main__':
    unittest.main()
