"""Isolation and required side effects of the compiler-cache build environment."""
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build


class BuildEnvironmentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.cache = Path(self.temp.name)/'cache'
        self.root = Path(self.temp.name)/'source'

    def test_checkouts_never_share_targets_or_build_directories(self):
        a, _ = build.environment(self.root, self.cache, 'none', {})
        b, _ = build.environment(self.root/'other', self.cache, 'none', {})
        self.assertNotEqual(a['CARGO_TARGET_DIR'], b['CARGO_TARGET_DIR'])
        self.assertNotEqual(a['CARGO_BUILD_BUILD_DIR'], b['CARGO_BUILD_BUILD_DIR'])

    def test_inherited_shared_intermediates_are_replaced(self):
        target = self.cache/'owned'
        env, _ = build.environment(self.root, self.cache, 'none', {
            'CARGO_TARGET_DIR': str(target), 'CARGO_BUILD_BUILD_DIR': '/shared/unsafe'})
        self.assertEqual(env['CARGO_TARGET_DIR'], str(target))
        self.assertEqual(env['CARGO_BUILD_BUILD_DIR'], str(target/'build'))

    def test_relative_target_is_relative_to_the_checkout(self):
        env, _ = build.environment(self.root, self.cache, 'none', {'CARGO_TARGET_DIR': 'own'})
        self.assertEqual(env['CARGO_TARGET_DIR'], str(self.root/'own'))

    def test_target_ownership_rejects_another_checkout(self):
        build.claim_target(self.cache, self.root)
        with self.assertRaisesRegex(ValueError, 'another checkout'):
            build.claim_target(self.cache, self.root/'other')

    def test_concurrent_claims_from_the_same_checkout_succeed(self):
        with ThreadPoolExecutor(max_workers=8) as pool:
            list(pool.map(lambda _: build.claim_target(self.cache, self.root), range(32)))
        self.assertEqual((self.cache/'.unbound-source-root').read_text(), str(self.root)+'\n')

    @patch.object(build.shutil, 'which', return_value='/installed/kache')
    def test_kache_never_replays_or_shares_build_script_side_effects(self, _):
        env, _ = build.environment(self.root, self.cache, 'kache', {
            'KACHE_BUILD_SCRIPT_CACHE': '1', 'KACHE_BUILD_SCRIPT_HERMETIC': '1',
            'KACHE_SEED_NEW_TARGETS': '1', 'KACHE_TRUST_DAEMON_EXECUTION': '1'})
        for key in ['KACHE_BUILD_SCRIPT_CACHE', 'KACHE_BUILD_SCRIPT_HERMETIC',
                    'KACHE_SEED_NEW_TARGETS', 'KACHE_SEED_INCREMENTAL',
                    'KACHE_AUTO_RECOVER_TARGETS', 'KACHE_TRUST_DAEMON_EXECUTION']:
            self.assertEqual(env[key], '0', key)
        self.assertEqual(env['KACHE_BASE_DIR'], str(self.root))

    @patch.object(build.shutil, 'which', return_value='/installed/sccache')
    def test_explicit_native_compilers_and_job_budget_are_preserved(self, _):
        env, _ = build.environment(self.root, self.cache, 'sccache', {
            'CC': 'custom-cc', 'CXX': 'custom-cxx', 'CARGO_BUILD_JOBS': '2'})
        self.assertEqual(env['CC'], 'custom-cc')
        self.assertEqual(env['CXX'], 'custom-cxx')
        self.assertEqual(env['CARGO_BUILD_JOBS'], '2')


if __name__ == '__main__':
    unittest.main()
