"""Real local remotes exercise push success, rejection, retry and train coalescing."""
import json
import fcntl
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import time
import unittest
from unittest import mock

import land_push


class PushTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo, self.remote, self.state = [self.root/name for name in ('repo', 'remote', 'state')]
        subprocess.run(['git', 'init', '-q', '--bare', str(self.remote)], check=True)
        self.repo.mkdir()
        self.git('init', '-q', '-b', 'main')
        self.git('config', 'user.name', 'Test')
        self.git('config', 'user.email', 'test@example.invalid')
        self.git('remote', 'add', 'origin', str(self.remote))
        self.add('initial')

    def git(self, *args):
        return land_push.git(self.repo, *args).stdout.strip()

    def add(self, text):
        (self.repo/'content').write_text(text)
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): push control')
        head = self.git('rev-parse', 'HEAD')
        land_push.receipts(self.state, lambda rows: rows.append({'new': head, 'request_id': text}))
        return head

    def rows(self):
        return [json.loads(line) for line in (self.state/'landed.jsonl').read_text().splitlines()]

    def test_origin_push_coalesces_train_and_reports_each_receipt(self):
        self.add('second')
        target = self.add('third')
        hook = self.remote/'hooks/pre-receive'
        hook.write_text('#!/bin/sh\necho attempt >> "'+str(self.root/'attempts')+'"\n')
        hook.chmod(0o755)
        land_push.enqueue(self.repo, self.state, launch=False)
        self.assertTrue(all(row['push']['status'] == 'pending' for row in self.rows()))
        land_push.deliver(self.repo)
        remote = subprocess.check_output(['git', '--git-dir', str(self.remote), 'rev-parse', 'main'], text=True).strip()
        self.assertEqual(remote, target)
        self.assertEqual((self.root/'attempts').read_text().splitlines(), ['attempt'])
        self.assertTrue(all(row['push']['status'] == 'pushed' for row in self.rows()))

    def test_rejection_is_durable_does_not_change_main_and_retries(self):
        target = self.git('rev-parse', 'main')
        hook = self.remote/'hooks/pre-receive'
        hook.write_text('#!/bin/sh\nexit 1\n')
        hook.chmod(0o755)
        land_push.enqueue(self.repo, self.state, launch=False)
        land_push.deliver(self.repo, delay=lambda _: None)
        value = json.loads(land_push.paths(self.repo)[0].read_text())
        self.assertEqual((value['status'], value['attempts']), ('failed', 3))
        self.assertEqual(self.rows()[0]['push']['status'], 'failed')
        self.assertEqual(self.git('rev-parse', 'main'), target)
        hook.unlink()
        land_push.deliver(self.repo, delay=lambda _: None)
        self.assertEqual(self.rows()[0]['push']['status'], 'pushed')

    def test_new_train_arriving_during_push_is_not_lost(self):
        first = self.git('rev-parse', 'main')
        land_push.enqueue(self.repo, self.state, launch=False)
        original = land_push.git
        added = []
        def concurrent(repo, *args, **kwargs):
            result = original(repo, *args, **kwargs)
            if args[0] == 'push' and not added:
                added.append(True)
                self.add('new train')
                land_push.enqueue(self.repo, self.state, launch=False)
            return result
        with mock.patch.object(land_push, 'git', side_effect=concurrent):
            land_push.deliver(self.repo)
        self.assertNotEqual(self.git('rev-parse', 'main'), first)
        self.assertTrue(all(row['push']['status'] == 'pushed' for row in self.rows()))

    def test_no_force_push_when_remote_diverges(self):
        land_push.enqueue(self.repo, self.state, launch=False)
        land_push.deliver(self.repo)
        initial = self.git('rev-parse', 'main')
        self.add('remote future')
        self.git('push', '-q', 'origin', 'main')
        remote = self.git('rev-parse', 'main')
        self.git('checkout', '-q', '--detach', initial)
        self.git('branch', '-f', 'main', initial)
        self.add('local divergence')
        # add() commits on detached HEAD; point main at that independent commit.
        self.git('branch', '-f', 'main', self.git('rev-parse', 'HEAD'))
        local = self.git('rev-parse', 'main')
        land_push.enqueue(self.repo, self.state, launch=False)
        land_push.deliver(self.repo, attempts=1)
        self.assertEqual(self.git('rev-parse', 'main'), local)
        self.assertEqual(subprocess.check_output(['git', '--git-dir', str(self.remote), 'rev-parse', 'main'], text=True).strip(), remote)

    def test_disabled_push_and_launcher_failure_are_visible(self):
        with mock.patch.dict(os.environ, UNBOUND_LAND_NO_PUSH='1'):
            value = land_push.enqueue(self.repo, self.state)
        self.assertEqual(value['status'], 'disabled')
        self.assertEqual(self.rows()[0]['push']['status'], 'disabled')
        real = subprocess.Popen
        def launch(*args, **kwargs):
            if args[0][0] != 'git':
                raise OSError('cannot launch')
            return real(*args, **kwargs)
        with mock.patch.object(land_push.subprocess, 'Popen', side_effect=launch):
            self.assertIn('cannot launch', land_push.enqueue(self.repo, self.state)['launch_error'])
        self.assertEqual(json.loads(land_push.paths(self.repo)[0].read_text())['status'], 'pending')

    def test_network_push_does_not_need_the_land_lock_and_receipts_wait_for_old_writers(self):
        land_push.enqueue(self.repo, self.state, launch=False)
        with (self.state/'land.lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            worker = threading.Thread(target=land_push.deliver, args=(self.repo,))
            worker.start()
            try:
                deadline = time.monotonic()+5
                while time.monotonic() < deadline:
                    found = subprocess.run(['git', '--git-dir', str(self.remote), 'rev-parse', '--verify', 'main'],
                                           capture_output=True)
                    if found.returncode == 0:
                        break
                    time.sleep(.01)
                self.assertEqual(found.returncode, 0)
                self.assertTrue(worker.is_alive())
                # Simulate an older coordinator's append while holding land.lock.
                with (self.state/'landed.jsonl').open('a') as stream:
                    stream.write(json.dumps({'new': self.git('rev-parse', 'main'), 'request_id': 'old-writer'})+'\n')
            finally:
                fcntl.flock(lock, fcntl.LOCK_UN)
                worker.join(timeout=10)
        self.assertFalse(worker.is_alive())
        self.assertEqual(len(self.rows()), 2)
        self.assertTrue(all(row['push']['status'] == 'pushed' for row in self.rows()))


if __name__ == '__main__':
    unittest.main()
