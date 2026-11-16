#!/usr/bin/env python3
"""The post-commit hook pushes a branch only to its own name on its remote."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import git_hooks


class PostCommitTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.remote, self.repo = root/'remote.git', root/'repo'
        subprocess.run(['git', 'init', '-q', '--bare', '-b', 'main', str(self.remote)], check=True)
        self.repo.mkdir()
        self.git('init', '-q', '-b', 'main')
        self.git('config', 'user.email', 'infra-test@example.invalid')
        self.git('config', 'user.name', 'Infra test')
        self.git('remote', 'add', 'origin', str(self.remote))
        self.commit('base')
        self.git('push', '-q', '-u', 'origin', 'main')
        self.assertEqual(git_hooks.install(self.repo), ['post-commit'])

    def git(self, *args):
        return subprocess.run(['git', *args], cwd=self.repo, check=True, capture_output=True,
                              text=True).stdout.strip()

    def remote_head(self, branch):
        result = subprocess.run(['git', '--git-dir', str(self.remote), 'rev-parse', '--verify', '--quiet',
                                 f'refs/heads/{branch}'], capture_output=True, text=True)
        return result.stdout.strip() or None

    def commit(self, message):
        (self.repo/'file.txt').write_text(message)
        self.git('add', 'file.txt')
        self.git('commit', '-qm', message)
        return self.git('rev-parse', 'HEAD')

    def pushed(self, branch, commit, seconds=20):
        """Whether the background push brought commit to the remote branch in time."""
        deadline = time.monotonic()+seconds
        while time.monotonic() < deadline:
            if self.remote_head(branch) == commit:
                return True
            time.sleep(0.1)
        return False

    def log(self):
        path = Path(self.git('rev-parse', '--path-format=absolute', '--git-common-dir'))/'auto-push.log'
        return path.read_text() if path.exists() else ''

    def wait_for_log(self, text, seconds=20):
        deadline = time.monotonic()+seconds
        while text not in self.log() and time.monotonic() < deadline:
            time.sleep(0.1)
        return text in self.log()

    def test_a_branch_tracking_its_own_name_is_pushed(self):
        self.git('switch', '-q', '-c', 'feature')
        self.git('push', '-q', '-u', 'origin', 'feature')
        commit = self.commit('feature work')
        self.assertTrue(self.pushed('feature', commit))
        main = self.git('rev-parse', 'main')
        self.git('switch', '-q', 'main')
        self.assertEqual(self.remote_head('main'), main)

    def test_main_tracking_main_is_pushed(self):
        commit = self.commit('main work')
        self.assertTrue(self.pushed('main', commit))

    def test_a_work_branch_tracking_main_is_never_pushed(self):
        main = self.remote_head('main')
        self.git('switch', '-q', '-c', 'slot/work', '--track', 'origin/main')
        self.commit('work')
        self.assertTrue(self.wait_for_log('slot/work: upstream origin refs/heads/main is not its own branch'))
        self.assertEqual(self.remote_head('main'), main)

    def test_a_branch_tracking_another_name_or_a_local_branch_is_not_pushed(self):
        self.git('push', '-q', 'origin', 'main:other')
        other = self.remote_head('other')
        self.git('switch', '-q', '-c', 'work', '--track', 'origin/other')
        self.commit('work')
        self.assertTrue(self.wait_for_log('work: upstream origin refs/heads/other is not its own branch'))
        self.git('switch', '-q', '-c', 'local', '--track', 'main')
        self.commit('local work')
        self.assertTrue(self.wait_for_log('local: upstream . refs/heads/main is not its own branch'))
        self.assertEqual(self.remote_head('other'), other)
        self.assertIsNone(self.remote_head('local'))
        self.assertIsNone(self.remote_head('work'))

    def test_a_branch_without_an_upstream_is_left_alone(self):
        self.git('switch', '-q', '-c', 'slot/plain')
        self.commit('plain')
        time.sleep(0.5)
        self.assertIsNone(self.remote_head('slot/plain'))
        self.assertNotIn('slot/plain', self.log())

    def test_install_rewrites_only_a_hook_that_differs(self):
        hook = git_hooks.hooks_dir(self.repo)/'post-commit'
        self.assertEqual(git_hooks.install(self.repo), [])
        hook.write_text('#!/bin/sh\ngit push origin HEAD:main\n')
        self.assertEqual(git_hooks.install(self.repo), ['post-commit'])
        self.assertEqual(hook.read_text(), git_hooks.POST_COMMIT)
        self.assertTrue(os.access(hook, os.X_OK))
        self.assertEqual([path.name for path in hook.parent.iterdir() if path.name.startswith('.')], [])


if __name__ == '__main__':
    unittest.main()
