"""Isolation and required side effects of the compiler-cache build environment."""
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import subprocess
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



class CacheDaemonTests(unittest.TestCase):
    """The compiler-cache daemon serves every later build of its cache, so a build starts it
    through the user service manager instead of letting a (possibly lowered-priority) compiler do it."""
    ENV = {'KACHE_CACHE_DIR': '/cache/kache', 'KACHE_LOCAL_ONLY': '1', 'PATH': '/bin', 'HOME': '/home/x',
           'CARGO_TARGET_DIR': '/target'}

    def fake(self, running, failing=()):
        calls = []
        state = {'running': running}

        def run(command, **kwargs):
            calls.append(command)
            if command[1:3] == ['daemon', 'status']:
                return subprocess.CompletedProcess(command, 0, json.dumps({'daemon_running': state['running']}), '')
            if command[0] in failing:
                raise FileNotFoundError(command[0])
            state['running'] = True
            return subprocess.CompletedProcess(command, 0)
        return calls, run

    def test_a_running_daemon_is_left_alone(self):
        calls, run = self.fake(True)
        with patch.object(build.subprocess, 'run', run):
            build.start_cache_daemon('/k/kache', self.ENV)
        self.assertEqual(calls, [['/k/kache', 'daemon', 'status', '--json']])

    def test_a_missing_daemon_starts_under_the_service_manager_at_its_priority(self):
        calls, run = self.fake(False)
        with patch.object(build.subprocess, 'run', run):
            build.start_cache_daemon('/k/kache', self.ENV)
        start = calls[1]
        self.assertEqual(start[:3], ['systemd-run', '--user', '--quiet'])
        self.assertIn('--property=Nice=0', start)
        self.assertEqual(start[-3:], ['/k/kache', 'daemon', 'run'])
        self.assertIn('--setenv=KACHE_CACHE_DIR=/cache/kache', start)
        self.assertIn('--setenv=KACHE_LOCAL_ONLY=1', start)
        self.assertNotIn('--setenv=CARGO_TARGET_DIR=/target', start)
        # One unit per cache directory: a second build of the same cache cannot start a twin.
        other = dict(self.ENV, KACHE_CACHE_DIR='/other/kache')
        calls2, run2 = self.fake(False)
        with patch.object(build.subprocess, 'run', run2):
            build.start_cache_daemon('/k/kache', other)
        unit = [arg for arg in start if arg.startswith('--unit=')]
        self.assertNotEqual(unit, [arg for arg in calls2[1] if arg.startswith('--unit=')])
        self.assertEqual(calls[-1], ['/k/kache', 'daemon', 'status', '--json'])

    def test_without_a_service_manager_the_daemon_starts_as_before(self):
        calls, run = self.fake(False, failing=('systemd-run',))
        with patch.object(build.subprocess, 'run', run):
            build.start_cache_daemon('/k/kache', self.ENV)
        self.assertIn(['/k/kache', 'daemon', 'start'], calls)

    def test_a_daemon_that_never_answers_does_not_fail_the_build(self):
        def run(command, **kwargs):
            raise subprocess.TimeoutExpired(command, 30)
        with patch.object(build.subprocess, 'run', run), patch.object(build, 'DAEMON_READY_SECONDS', .3):
            build.start_cache_daemon('/k/kache', self.ENV)

    def test_kache_builds_start_the_daemon_before_the_job(self):
        order = []
        with tempfile.TemporaryDirectory() as temporary, \
             patch.object(sys, 'argv', ['build.py', '--direct', '--', 'build']), \
             patch.object(build, 'build_root', return_value=Path(temporary)/'builds'), \
             patch.object(build.Path, 'cwd', return_value=Path(temporary)), \
             patch.object(build, 'environment', return_value=({'CARGO_TARGET_DIR': str(Path(temporary)/'t'),
                                                               'RUSTC_WRAPPER': '/k/kache'}, 'kache')), \
             patch.object(build, 'start_cache_daemon', side_effect=lambda *a: order.append('daemon')), \
             patch.object(build.os, 'execvpe', side_effect=lambda *a: order.append('exec')), \
             patch.object(build.sys, 'stderr'):
            build.main()
        self.assertEqual(order, ['daemon', 'exec'])

if __name__ == '__main__':
    unittest.main()
