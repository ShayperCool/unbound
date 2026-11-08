#!/usr/bin/env python3
"""Touched-crate coverage for the serialized landing gate."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import os

sys.path.insert(0, str(Path(__file__).resolve().parent))
import land


class TouchedCratesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        source = Path(__file__).resolve().parents[2]
        # Use the real native build recipes and importer dependencies so the
        # model-cache guard cannot silently stop being selected as they evolve.
        for name in ['unbound-assets', 'unbound-host']:
            for file in ['Cargo.toml', 'build.rs']:
                self.write(f'crates/{name}/{file}', (source/'crates'/name/file).read_text())
        self.write('crates/core/Cargo.toml', '[package]\nname = "unbound-core"\nversion = "0.1.0"\n')
        self.write('Cargo.toml', '[workspace]\nmembers = ["crates/*"]\n')
        self.write('Cargo.lock', 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.0"\n')
        self.git('init', '-q')
        self.git('config', 'user.email', 'infra-test@example.invalid')
        self.git('config', 'user.name', 'Infra test')
        self.git('config', 'core.hooksPath', '/dev/null')
        self.commit()
        self.base = self.git('rev-parse', 'HEAD')
        self.gate = land.Gate(self.root, self.base, [], False, self.root/'gate.log')

    def write(self, path, text):
        target = self.root/path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)

    def git(self, *args):
        return subprocess.run(['git', *args], cwd=self.root, check=True,
                              capture_output=True, text=True).stdout.strip()

    def commit(self):
        self.git('add', '.')
        self.git('commit', '-qm', 'test(infra): selection fixture')

    def test_rust_sources_select_manifest_names_and_deduplicate(self):
        self.assertEqual(self.gate.touched_crates([
            'crates/core/src/lib.rs', 'crates/core/src/other.rs',
            'crates/unbound-assets/src/model/mod.rs']), ['unbound-assets', 'unbound-core'])

    def test_native_sources_select_their_build_crates(self):
        for path, expected in [
            ('native/glu_tess/src/tess.c', ['unbound-assets']),
            ('native/unbound_model_tess.c', ['unbound-assets']),
            ('native/vorbis/lib/block.c', ['unbound-assets']),
            ('native/unbound_physx.cpp', ['unbound-host']),
            ('native/physx/source/file.cpp', ['unbound-host']),
            ('native/unrelated/file.c', []),
        ]:
            with self.subTest(path=path):
                self.assertEqual(self.gate.touched_crates([path]), expected)

    def test_locked_importer_dependency_bump_selects_model_guard(self):
        self.write('Cargo.lock', 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.1"\n')
        self.commit()
        self.assertEqual(self.gate.touched_crates(['Cargo.lock']), ['unbound-assets'])

    def test_unrelated_lock_change_does_not_select_importer(self):
        self.write('Cargo.lock', 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.0"\n'
                   + '\n[[package]]\nname = "unrelated"\nversion = "1.0.0"\n')
        self.commit()
        self.assertEqual(self.gate.touched_crates(['Cargo.lock']), [])

    def test_transitive_dependency_change_selects_importer(self):
        dependency = '\n[[package]]\nname = "decoder"\nversion = "1.0.0"\nchecksum = "aaa"\n'
        importer = 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.0"\ndependencies = ["decoder"]\n'
        self.write('Cargo.lock', importer+dependency)
        self.commit()
        self.gate.base = self.git('rev-parse', 'HEAD')
        self.write('Cargo.lock', importer+dependency.replace('aaa', 'bbb'))
        self.commit()
        self.assertEqual(self.gate.touched_crates(['Cargo.lock']), ['unbound-assets'])

    def test_renamed_target_dependency_selects_consumer(self):
        self.write('crates/core/Cargo.toml', '[package]\nname = "unbound-core"\nversion = "0.1.0"\n'
                   + '[target.\'cfg(unix)\'.build-dependencies]\nalias = { package = "ufbx", version = "0.11" }\n')
        self.write('Cargo.lock', 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.1"\n')
        self.commit()
        self.assertEqual(self.gate.touched_crates(['Cargo.lock']), ['unbound-assets', 'unbound-core'])

    def test_crate_manifest_selects_its_crate(self):
        self.assertEqual(self.gate.touched_crates(['crates/unbound-assets/Cargo.toml']), ['unbound-assets'])

    def test_workspace_manifest_selects_all_crates(self):
        self.assertEqual(self.gate.touched_crates(['Cargo.toml']),
                         ['unbound-assets', 'unbound-core', 'unbound-host'])

    def test_docs_only_select_no_crates(self):
        self.assertEqual(self.gate.touched_crates(['README.md', 'docs/ARCHITECTURE.md']), [])

    def test_managed_only_select_no_crates(self):
        self.assertEqual(self.gate.touched_crates(['managed/UnityEngine/Object.cs']), [])

    def test_probe_inputs_select_crates_whose_tests_read_them(self):
        # The real model output guard, so its probe list cannot silently stop selecting it.
        source = Path(__file__).resolve().parents[2]
        guard = 'crates/unbound-assets/src/model/version_guard.rs'
        self.write(guard, (source/guard).read_text())
        self.write('crates/core/src/lib.rs',
                   'const R: &str = include_str!("../../../tests/projects/sample/Reference/a");\n')
        for path, expected in [
            ('tests/projects/humanoid/Assets/Resources/HumanA.fbx.meta', ['unbound-assets']),
            ('tests/projects/modelimport/Assets/Models/Cube.fbx', ['unbound-assets']),
            ('tests/projects/sample/README.md', ['unbound-core']),
            ('tests/projects/sampled/README.md', []),
            ('tests/projects/unreferenced/Assets/Probe.cs', []),
            ('tests/projects/README.md', []),
        ]:
            with self.subTest(path=path):
                self.assertEqual(self.gate.touched_crates([path]), expected)

    def test_deleted_crate_is_not_tested(self):
        self.assertEqual(self.gate.touched_crates(['crates/removed/src/lib.rs']), [])


class GatePriorityTests(unittest.TestCase):
    def test_only_lock_holding_gates_mark_all_their_child_jobs(self):
        root = Path('/tmp/priority-control')
        with mock.patch.dict(os.environ, {'UNBOUND_LAND_GATE': '1'}):
            check = land.Gate(root, 'HEAD', [], False, root/'check.log')
            gate = land.Gate(root, 'HEAD', [], False, root/'land.log', priority=True)
        self.assertNotIn('UNBOUND_LAND_GATE', check.env)
        self.assertEqual(gate.env['UNBOUND_LAND_GATE'], '1')


if __name__ == '__main__':
    unittest.main()
