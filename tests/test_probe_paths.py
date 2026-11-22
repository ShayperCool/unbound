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
        # A release set not recorded yet under a flat-looking Reference keeps the release layout.
        self.assertEqual(read(flat/'Reference/unity-R/a.png', publishing=True), flat/'Reference/unity-R/metal/a.png')
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


class PrivateLabelTests(unittest.TestCase):
    def test_concurrent_runs_get_their_own_labels(self):
        with patch.object(probe_paths.time, 'strftime', return_value='check-20261119-101500-'):
            with patch.object(probe_paths.os, 'getpid', return_value=41):
                first = probe_paths.private_label()
            with patch.object(probe_paths.os, 'getpid', return_value=42):
                second = probe_paths.private_label()
        self.assertEqual(first, 'check-20261119-101500-41')
        self.assertNotEqual(first, second)
        self.assertNotEqual(probe_paths.run_root('p', first), probe_paths.run_root('p', second))
        self.assertTrue(probe_paths.private_label('gate').startswith('gate-'))


class ProjectCopyTests(unittest.TestCase):
    """A run's private project copy: what the run writes stays in it (tests/probe_paths.py)."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.source = Path(self.temp.name)/'projects/probe'
        for folder in ('Assets/Scripts', 'Packages', 'ProjectSettings', 'Reference', 'Library/PackageCache/pkg',
                       'LibraryUnbound'):
            (self.source/folder).mkdir(parents=True)
        (self.source/'Assets/Scripts/Probe.cs').write_text('class Probe {}')
        (self.source/'ProjectSettings/ProjectSettings.asset').write_text('settings')
        (self.source/'Reference/unity-R.jsonl').write_text('reference')
        (self.source/'LibraryUnbound/cache').write_text('private to the source')
        (self.source/'run.py').write_text('driver')
        (self.source/'Content/bundles').mkdir(parents=True)
        (self.source/'Content/bundles/crc.txt').write_text('crc')
        (self.source/'Probe.csproj').write_text('generated')
        (self.source/'Temp').mkdir()
        (self.source/'Packages/com.probe.embedded').mkdir()
        (self.source/'Packages/manifest.json').write_text(
            '{"dependencies": {"local": "file:../../local", "registry": "1.0.0", '
            '"embedded": "file:com.probe.embedded"}}')

    def test_a_run_writes_only_its_copy(self):
        project = probe_paths.project_copy(self.source, Path(self.temp.name)/'run/project')
        self.assertEqual((project/'Assets/Scripts/Probe.cs').read_text(), 'class Probe {}')
        # What a run writes into the project, as the Editor writes it, stays in the copy.
        (project/'Assets/Scripts/Probe.cs.meta').write_text('guid')
        (project/'ProjectSettings/ProjectSettings.asset').write_text('changed')
        (project/'Packages/packages-lock.json').write_text('{}')
        self.assertFalse((self.source/'Assets/Scripts/Probe.cs.meta').exists())
        self.assertEqual((self.source/'ProjectSettings/ProjectSettings.asset').read_text(), 'settings')
        self.assertFalse((self.source/'Packages/packages-lock.json').exists())
        # Inputs only: the references and resolved packages are the source's, the caches are not.
        self.assertTrue((project/'Reference').is_symlink())
        self.assertEqual((project/'Reference/unity-R.jsonl').read_text(), 'reference')
        self.assertTrue((project/'Library/PackageCache').is_symlink())
        self.assertFalse((project/'LibraryUnbound').exists())
        # Other root inputs (a probe's Content/, its scripts) are the source's too.
        self.assertEqual((project/'Content/bundles/crc.txt').read_text(), 'crc')
        self.assertTrue((project/'Content').is_symlink() and (project/'run.py').is_symlink())
        self.assertFalse((project/'Probe.csproj').exists() or (project/'Temp').exists())
        manifest = (project/'Packages/manifest.json').read_text()
        self.assertIn('"file:' + str((self.source/'Packages/../../local').resolve()) + '"', manifest)
        self.assertIn('"registry": "1.0.0"', manifest)
        # An embedded package is copied with Packages/: its path stays as authored.
        self.assertIn('"embedded": "file:com.probe.embedded"', manifest)

    def test_steps_of_one_run_share_a_copy_and_fresh_replaces_it(self):
        target = Path(self.temp.name)/'run/project'
        project = probe_paths.project_copy(self.source, target)
        (project/'Assets/Scripts/Probe.cs.meta').write_text('guid')
        self.assertTrue((probe_paths.project_copy(self.source, target)/'Assets/Scripts/Probe.cs.meta').exists())
        (self.source/'Assets/Scripts/Probe.cs').write_text('class Probe { int changed; }')
        fresh = probe_paths.project_copy(self.source, target, fresh=True)
        self.assertFalse((fresh/'Assets/Scripts/Probe.cs.meta').exists())
        self.assertEqual((fresh/'Assets/Scripts/Probe.cs').read_text(), 'class Probe { int changed; }')
        self.assertEqual((self.source/'Reference/unity-R.jsonl').read_text(), 'reference')

    def test_an_editor_copy_has_its_own_package_cache(self):
        project = probe_paths.project_copy(self.source, Path(self.temp.name)/'editor', package_cache=False)
        self.assertFalse((project/'Library').exists())
        self.assertTrue((project/'Reference').is_symlink())

    def test_ignored_leftovers_are_not_linked(self):
        import subprocess
        subprocess.run(['git', 'init', '-q'], cwd=self.source, check=True)
        (self.source/'.gitignore').write_text('Evidence*/\n')
        (self.source/'Evidence-old').mkdir()
        with patch.object(probe_paths, 'CHECKOUT', self.source.resolve()):
            project = probe_paths.project_copy(self.source, Path(self.temp.name)/'run/project')
        self.assertFalse((project/'Evidence-old').exists())
        self.assertTrue((project/'Content').is_symlink())

    def test_the_checkout_is_recognised(self):
        self.assertTrue(probe_paths.checkout_project(probe_paths.CHECKOUT/'tests/projects/physics'))
        self.assertFalse(probe_paths.checkout_project(self.source))


if __name__ == '__main__':
    unittest.main()
