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

    def test_reap_removes_clean_idle_worktrees_and_retains_their_branches(self):
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
        unlanded_head = self.git('rev-parse', 'HEAD', cwd=unlanded)
        unlanded_target = self.make_target(unlanded)
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
        self.assertIn('3 would be removed', out)
        self.assertTrue(unlanded.exists() and unlanded_target.exists())

        self.run_slot('reap')
        self.assertFalse(finished.exists())
        self.assertFalse(finished_target.exists())
        self.assertFalse(orphan.exists())
        self.assertFalse(unlanded.exists())
        self.assertFalse(unlanded_target.exists())
        self.assertEqual(self.git('rev-parse', 't3/unlanded'), unlanded_head)
        for kept in (dirty, active, linked, self.repo, self.slot_path(1), slot_target):
            self.assertTrue(kept.exists(), kept)
        self.assertIn('t3/finished', self.git('branch', '--list', 't3/finished'))

    def test_reap_keeps_unmerged_dirty_active_slot_and_linked_worktrees(self):
        self.run_slot('create', '--count', '1')
        kept = [self.slot_path(1)]
        for name in ('dirty', 'staged', 'untracked', 'active', 'linked'):
            path = Path(self.temp.name).resolve()/name
            self.git('worktree', 'add', '-q', '-b', f't3/{name}', str(path), 'main')
            kept.append(path)
        heads = {}
        for path in kept:
            (path/'a.txt').write_text('committed\n')
            self.git('commit', '-qam', 'unmerged', cwd=path)
            heads[path] = self.git('rev-parse', 'HEAD', cwd=path)
            if path.name != 'active':
                self.make_old(path)
        (kept[1]/'a.txt').write_text('tracked work\n')
        (kept[2]/'a.txt').write_text('staged work\n')
        self.git('add', '.', cwd=kept[2])
        self.make_old(kept[2])
        (kept[3]/'new.txt').write_text('untracked work\n')
        runs = Path(self.env['XDG_CACHE_HOME'])/'unbound/runs'
        runs.mkdir(parents=True)
        (runs/'linked').symlink_to(kept[-1]/'a.txt')
        code, out = self.run_slot('reap', '--verbose')
        self.assertEqual(code, 0)
        self.assertIn('0 removed', out)
        self.assertIn('6 kept with unmerged commits', out)
        for path in kept:
            self.assertTrue(path.exists())
            self.assertEqual(heads[path], self.git('rev-parse', 'HEAD', cwd=path))
        self.assertEqual((kept[1]/'a.txt').read_text(), 'tracked work\n')
        self.assertEqual((kept[2]/'a.txt').read_text(), 'staged work\n')
        self.assertEqual((kept[3]/'new.txt').read_text(), 'untracked work\n')

    def test_reap_preserves_detached_unmerged_commits_before_removing_checkout(self):
        path = Path(self.temp.name).resolve()/'detached'
        self.git('worktree', 'add', '-q', '--detach', str(path), 'main')
        (path/'a.txt').write_text('detached work\n')
        self.git('commit', '-qam', 'unmerged', cwd=path)
        head = self.git('rev-parse', 'HEAD', cwd=path)
        self.make_old(path)
        _, out = self.run_slot('reap', '--dry-run')
        self.assertIn('would preserve detached commits', out)
        self.assertTrue(path.exists())
        self.assertEqual(self.git('branch', '--list', 'reaped-worktree/*'), '')
        self.run_slot('reap')
        self.assertFalse(path.exists())
        self.assertEqual(self.git('rev-parse', f'reaped-worktree/{head}'), head)

    def test_reap_keeps_deep_run_links_through_a_cache_directory_alias(self):
        base = Path(self.temp.name).resolve()
        linked = base/'deep-linked'
        self.git('worktree', 'add', '-q', '-b', 't3/deep-linked', str(linked), 'main')
        (linked/'a.txt').write_text('unmerged\n')
        self.git('commit', '-qam', 'unmerged', cwd=linked)
        target = self.make_target(linked)
        self.make_old(linked)
        cache = Path(self.env['XDG_CACHE_HOME'])/'unbound'
        external = base/'external-runs'
        external.mkdir()
        cache.mkdir(parents=True)
        (cache/'validation').symlink_to(external, target_is_directory=True)
        deep = external/'runs/probe/runs/label/project/Assets'
        deep.parent.mkdir(parents=True)
        deep.symlink_to(linked, target_is_directory=True)
        # Internal aliases and cycles must not make the walk infinite or omit a link.
        (external/'cycle').symlink_to(external, target_is_directory=True)
        (cache/'cycle').symlink_to(cache, target_is_directory=True)
        _, out = self.run_slot('reap', '--verbose')
        self.assertTrue(linked.exists() and target.exists())
        self.assertIn('a symlink under the run caches points into it', out)
        self.assertIn('1 kept with unmerged commits', out)

    @unittest.skipIf(os.geteuid() == 0, 'root can read a mode-000 directory')
    def test_reap_refuses_to_delete_when_run_cache_links_cannot_be_scanned(self):
        base = Path(self.temp.name).resolve()
        checkout = base/'clean-idle'
        self.git('worktree', 'add', '-q', '-b', 't3/clean-idle', str(checkout), 'main')
        self.make_old(checkout)
        unreadable = Path(self.env['XDG_CACHE_HOME'])/'unbound/private-runs'
        unreadable.mkdir(parents=True)
        unreadable.chmod(0)
        try:
            with self.assertRaises(SystemExit) as raised:
                self.run_slot('reap')
            self.assertIn('refusing to reap worktrees', str(raised.exception))
            self.assertTrue(checkout.exists())
        finally:
            unreadable.chmod(0o700)

    def test_reap_keeps_both_source_and_destination_of_a_reference_chain(self):
        base = Path(self.temp.name).resolve()
        source, destination, unused = [base/name for name in ('source', 'destination', 'unused')]
        for path in (source, destination, unused):
            self.git('worktree', 'add', '-q', '-b', f't3/{path.name}', str(path), 'main')
            (path/'b.txt').write_text('unmerged\n')
            self.git('add', '.', cwd=path)
            self.git('commit', '-qm', 'unmerged', cwd=path)
        (source/'a.txt').unlink()
        (source/'a.txt').symlink_to(destination/'a.txt')
        self.git('add', '.', cwd=source)
        self.git('commit', '-qm', 'reference', cwd=source)
        for path in (source, destination, unused):
            self.make_old(path)
        cache = Path(self.env['XDG_CACHE_HOME'])/'unbound/runs'
        cache.mkdir(parents=True)
        (cache/'reference').symlink_to(source/'a.txt')
        self.run_slot('reap')
        self.assertTrue(source.exists() and destination.exists())
        self.assertFalse(unused.exists())

    def test_reap_keeps_every_intermediate_checkout_in_a_reference_chain(self):
        base = Path(self.temp.name).resolve()
        paths = [base/name for name in ('first', 'middle', 'last')]
        for path in paths:
            self.git('worktree', 'add', '-q', '-b', f't3/{path.name}', str(path), 'main')
        for source, destination in zip(paths, paths[1:]):
            (source/'a.txt').unlink()
            (source/'a.txt').symlink_to(destination/'a.txt')
            self.git('add', '.', cwd=source)
            self.git('commit', '-qm', 'reference', cwd=source)
        for path in paths:
            self.make_old(path)
        cache = Path(self.env['XDG_CACHE_HOME'])/'unbound/runs'
        cache.mkdir(parents=True)
        (cache/'reference').symlink_to(paths[0]/'a.txt')
        self.run_slot('reap')
        self.assertTrue(all(path.exists() for path in paths))

    def test_reap_follows_nested_directory_aliases(self):
        base = Path(self.temp.name).resolve()
        checkout = base/'linked'
        self.git('worktree', 'add', '-q', '-b', 't3/linked', str(checkout), 'main')
        self.make_old(checkout)
        external = base/'external'
        external.mkdir()
        (external/'Assets').symlink_to(checkout, target_is_directory=True)
        cache = Path(self.env['XDG_CACHE_HOME'])/'unbound/runs/probe'
        cache.mkdir(parents=True)
        (cache/'project').symlink_to(external, target_is_directory=True)
        self.run_slot('reap')
        self.assertTrue(checkout.exists())

    def test_reap_follows_references_inside_a_protected_checkout(self):
        base = Path(self.temp.name).resolve()
        source, destination = base/'source', base/'destination'
        for path in (source, destination):
            self.git('worktree', 'add', '-q', '-b', f't3/{path.name}', str(path), 'main')
        (source/'Assets').symlink_to(destination, target_is_directory=True)
        self.git('add', '.', cwd=source)
        self.git('commit', '-qm', 'reference', cwd=source)
        for path in (source, destination):
            self.make_old(path)
        cache = Path(self.env['XDG_CACHE_HOME'])/'unbound/runs'
        cache.mkdir(parents=True)
        (cache/'project').symlink_to(source, target_is_directory=True)
        self.run_slot('reap')
        self.assertTrue(source.exists() and destination.exists())
        self.assertTrue((cache/'project/Assets/a.txt').is_file())

    @unittest.skipIf(os.geteuid() == 0, 'root can read a mode-000 directory')
    def test_reap_refuses_an_inaccessible_run_cache_parent(self):
        base = Path(self.temp.name).resolve()
        checkout = base/'clean-idle'
        self.git('worktree', 'add', '-q', '-b', 't3/clean-idle', str(checkout), 'main')
        self.make_old(checkout)
        parent = Path(self.env['XDG_CACHE_HOME'])
        (parent/'unbound').mkdir(parents=True)
        parent.chmod(0)
        try:
            with self.assertRaises(SystemExit) as raised:
                self.run_slot('reap')
            self.assertIn('refusing to reap worktrees', str(raised.exception))
            self.assertTrue(checkout.exists())
        finally:
            parent.chmod(0o700)

    def test_parent_steps_after_a_directory_link_use_filesystem_resolution(self):
        base = Path(self.temp.name).resolve()
        checkout = base/'linked'
        self.git('worktree', 'add', '-q', '-b', 't3/linked', str(checkout), 'main')
        self.make_old(checkout)
        external = base/'external'
        (external/'child').mkdir(parents=True)
        (external/'reference').symlink_to(checkout/'a.txt')
        cache = Path(self.env['XDG_CACHE_HOME'])/'unbound/subdir'
        cache.mkdir(parents=True)
        (cache/'directory-alias').symlink_to(external/'child', target_is_directory=True)
        (cache/'reference').symlink_to('directory-alias/../reference')
        self.run_slot('reap')
        self.assertTrue(checkout.exists())


if __name__ == '__main__':
    unittest.main()
