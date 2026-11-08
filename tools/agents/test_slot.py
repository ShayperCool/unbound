#!/usr/bin/env python3
"""Persistent worker slots and finished-worktree cleanup (tools/agents/slot.py)."""
import contextlib
import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import slot  # noqa: E402
import build  # noqa: E402  (tools/, put on the path by slot)


class SlotTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        base = Path(self.temp.name).resolve()
        self.repo = base/'repo'
        self.repo.mkdir()
        self.env = {'UNBOUND_SLOT_ROOT': str(base/'slots'), 'UNBOUND_BUILD_ROOT': str(base/'builds'),
                    'XDG_CACHE_HOME': str(base/'xdg')}
        patcher = mock.patch.dict(os.environ, self.env)
        patcher.start()
        self.addCleanup(patcher.stop)
        self.git('init', '-q', '-b', 'main')
        self.git('config', 'user.email', 'infra-test@example.invalid')
        self.git('config', 'user.name', 'Infra test')
        self.git('config', 'core.hooksPath', '/dev/null')
        (self.repo/'.gitignore').write_text('/cache/\n')
        (self.repo/'a.txt').write_text('a\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'base')
        cwd = os.getcwd()
        os.chdir(self.repo)
        self.addCleanup(os.chdir, cwd)

    def git(self, *args, cwd=None):
        return subprocess.run(['git', *args], cwd=cwd or self.repo, check=True,
                              capture_output=True, text=True).stdout.strip()

    def run_slot(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = slot.main(list(args))
        return code, out.getvalue()

    def slot_path(self, number):
        return Path(self.env['UNBOUND_SLOT_ROOT'])/slot.slot_name(number)

    def make_old(self, path):
        git_dir = Path(self.git('rev-parse', '--absolute-git-dir', cwd=path))
        old = time.time() - 24*3600
        for name in ('index', 'HEAD'):
            if (git_dir/name).exists():
                os.utime(git_dir/name, (old, old))

    def make_target(self, checkout):
        target = build.default_target(checkout, build.build_root())
        (target/'release').mkdir(parents=True)
        (target/'.unbound-source-root').write_text(f'{checkout}\n')
        return target

    def test_create_makes_detached_slots_at_main_once(self):
        self.run_slot('create', '--count', '3')
        self.run_slot('create', '--count', '3')
        for number in (1, 2, 3):
            path = self.slot_path(number)
            self.assertEqual(self.git('rev-parse', 'HEAD', cwd=path), self.git('rev-parse', 'main'))
            self.assertEqual(self.git('branch', '--show-current', cwd=path), '')
        self.assertFalse(self.slot_path(4).exists())

    def test_prepare_starts_a_fresh_branch_and_keeps_ignored_caches(self):
        self.run_slot('create', '--count', '1')
        path = self.slot_path(1)
        (path/'cache').mkdir()
        (path/'cache'/'warm.bin').write_text('warm')
        self.run_slot('prepare', 'slot-01', 't3/task-a')
        self.assertEqual(self.git('branch', '--show-current', cwd=path), 't3/task-a')
        self.assertTrue((path/'cache'/'warm.bin').exists())

    def test_prepare_refuses_leftover_work_unless_discarded(self):
        self.run_slot('create', '--count', '1')
        path = self.slot_path(1)
        self.run_slot('prepare', 'slot-01', 't3/task-a')
        (path/'a.txt').write_text('edited\n')
        with self.assertRaises(SystemExit) as raised:
            self.run_slot('prepare', 'slot-01', 't3/task-b')
        self.assertIn('changed or untracked', str(raised.exception))
        self.git('commit', '-qam', 'unlanded', cwd=path)
        with self.assertRaises(SystemExit) as raised:
            self.run_slot('prepare', 'slot-01', 't3/task-b')
        self.assertIn('commits not on main', str(raised.exception))
        (path/'stray.txt').write_text('x')
        (path/'cache').mkdir()
        (path/'cache'/'warm.bin').write_text('warm')
        self.run_slot('prepare', 'slot-01', 't3/task-b', '--discard')
        self.assertEqual(self.git('branch', '--show-current', cwd=path), 't3/task-b')
        self.assertEqual((path/'a.txt').read_text(), 'a\n')
        self.assertFalse((path/'stray.txt').exists())
        self.assertTrue((path/'cache'/'warm.bin').exists())

    def test_prepare_rejects_paths_that_are_not_slots(self):
        with self.assertRaises(SystemExit):
            self.run_slot('prepare', 'slot-07', 't3/task-a')

    def test_reap_removes_only_finished_idle_worktrees_and_their_targets(self):
        self.run_slot('create', '--count', '1')
        base = Path(self.temp.name).resolve()
        finished = base/'wt-finished'
        self.git('worktree', 'add', '-q', '-b', 't3/finished', str(finished), 'main')
        finished_target = self.make_target(finished)
        unlanded = base/'wt-unlanded'
        self.git('worktree', 'add', '-q', '-b', 't3/unlanded', str(unlanded), 'main')
        (unlanded/'b.txt').write_text('b\n')
        self.git('add', '.', cwd=unlanded)
        self.git('commit', '-qm', 'not landed', cwd=unlanded)
        dirty = base/'wt-dirty'
        self.git('worktree', 'add', '-q', '-b', 't3/dirty', str(dirty), 'main')
        (dirty/'new.txt').write_text('n\n')
        active = base/'wt-active'
        self.git('worktree', 'add', '-q', '-b', 't3/active', str(active), 'main')
        linked = base/'wt-linked'
        self.git('worktree', 'add', '-q', '-b', 't3/linked', str(linked), 'main')
        runs = Path(self.env['XDG_CACHE_HOME'])/'unbound'/'validation'/'runs'
        runs.mkdir(parents=True)
        (runs/'probe').symlink_to(linked/'a.txt')
        for path in (finished, unlanded, dirty, linked, self.slot_path(1)):
            self.make_old(path)
        orphan = self.make_target(base/'gone')
        slot_target = self.make_target(self.slot_path(1))

        code, out = self.run_slot('reap', '--dry-run')
        self.assertEqual(code, 0)
        self.assertTrue(finished.exists() and finished_target.exists() and orphan.exists())
        self.assertIn('2 would be removed', out)

        self.run_slot('reap')
        self.assertFalse(finished.exists())
        self.assertFalse(finished_target.exists())
        self.assertFalse(orphan.exists())
        for kept in (unlanded, dirty, active, linked, self.slot_path(1), slot_target):
            self.assertTrue(kept.exists(), kept)
        self.assertIn('t3/finished', self.git('branch', '--list', 't3/finished'))


if __name__ == '__main__':
    unittest.main()
