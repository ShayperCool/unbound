#!/usr/bin/env python3
"""Disk janitor safety controls, using private fixtures only."""
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import janitor
import install_janitor


class JanitorTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.cache = self.root/'local/unbound'
        self.volume = self.root/'volume'
        self.builds = self.volume/'builds'
        self.repo = self.root/'checkout'
        self.repo.mkdir()
        for directory in [self.cache/'agents', self.builds/'targets', self.volume/'slots', self.cache/'runtimes']:
            directory.mkdir(parents=True)
        (self.cache/'agents/QUEUE.md').write_text('| Q1 | infra | old | landed | worker |\n| Q2 | infra | now | active | worker |\n')
        self.env = patch.dict(os.environ, {'XDG_CACHE_HOME': str(self.cache.parent),
                                          'UNBOUND_BUILD_ROOT': str(self.builds),
                                          'UNBOUND_SLOT_ROOT': str(self.volume/'slots'),
                                          'UNBOUND_RUNTIME_CACHE': str(self.cache/'runtimes')})
        self.env.start()
        self.addCleanup(self.env.stop)
        self.future = time.time()+10*86400
        self.j = janitor.Janitor(self.repo)
        self.j.states = janitor.task_states(self.cache/'agents/QUEUE.md')
        self.j.active = set()
        self.j.protected = []
        self.j.links = self.j.processes = set()
        self.silence = contextlib.redirect_stdout(io.StringIO())
        self.silence.__enter__()
        self.addCleanup(self.silence.__exit__, None, None, None)

    def directory(self, relative):
        path = self.volume/relative
        path.mkdir(parents=True)
        (path/'output').write_text('capture')
        return path

    @contextlib.contextmanager
    def guards(self):
        with patch.object(janitor, 'worktrees', return_value=[(self.repo, '', 'main')]), \
             patch.object(janitor, 'main_checkout', return_value=self.repo), \
             patch.object(janitor, 'process_paths', return_value=set()), \
             patch.object(janitor.runtime_snapshot, 'cleanup'), \
             patch.object(janitor.Janitor, 'kache_gc'), \
             patch.object(janitor.time, 'time', return_value=self.future):
            yield

    def test_symlinked_run_is_kept_and_unlinked_old_run_is_planned(self):
        linked = self.directory('unbound-validation/runs/probe-20261101-old')
        old = self.directory('unbound-validation/runs/probe-20261102-old')
        (self.cache/'retained').symlink_to(linked/'output')
        with self.guards():
            self.j.collect()
        self.assertEqual(self.j.removed, 1)
        self.assertTrue(linked.is_dir())
        self.assertTrue(old.is_dir())  # dry-run did not delete

    def test_active_unknown_and_conflicting_tasks_are_kept(self):
        for name in ('q2', 'q3', 'q4'):
            self.directory(name)
        with (self.cache/'agents/QUEUE.md').open('a') as stream:
            stream.write('| Q4 | infra | old | landed | worker |\n| Q4 | infra | new | active | worker |\n')
        with self.guards():
            self.j.apply = True
            self.j.collect()
        for name in ('q2', 'q3', 'q4'):
            self.assertTrue((self.volume/name/'output').is_file())
        self.assertEqual(self.j.removed, 0)

    def test_target_with_live_checkout_kept_orphan_removed(self):
        live = self.directory('builds/targets/live')
        orphan = self.directory('builds/targets/orphan')
        (live/'.unbound-source-root').write_text(str(self.repo))
        (orphan/'.unbound-source-root').write_text(str(self.root/'gone'))
        with self.guards():
            self.j.apply = True
            self.j.collect()
        self.assertTrue(live.is_dir())
        self.assertFalse(orphan.exists())

    def test_all_slots_main_and_train_are_protected(self):
        with self.guards():
            self.j.discover()
        self.addCleanup(self.j.watch.close)
        for n in range(1, 11):
            target = janitor.build.default_target(self.volume/f'slots/slot-{n:02}', self.builds)
            target.mkdir()
            self.assertEqual(self.j.reason(target, 24), 'protected checkout, slot or train')
        train = self.directory('land-trains/integration')
        self.assertEqual(self.j.reason(train, 24), 'protected checkout, slot or train')
        main_target = janitor.build.default_target(self.repo, self.builds)
        main_target.mkdir(exist_ok=True)
        self.assertEqual(self.j.reason(main_target, 24), 'protected checkout, slot or train')

    def test_recent_descendant_keeps_old_parent(self):
        path = self.directory('unbound-validation/runs/probe-20261101')
        os.utime(path/'output', (self.future, self.future))
        with patch.object(janitor.time, 'time', return_value=self.future):
            self.assertEqual(self.j.reason(path, 72), 'recently modified')

    def test_process_reference_and_symlink_chain_are_kept(self):
        path = self.directory('unbound-validation/runs/probe-20261101')
        alias = self.cache/'alias'
        alias.symlink_to(path, target_is_directory=True)
        (self.cache/'chain').symlink_to(alias/'output')
        self.j.links = janitor.reference_paths([self.cache])
        self.assertEqual(self.j.reason(path, 72), 'symlink reference')
        self.j.links = set()
        self.j.processes = {path/'output'}
        self.assertEqual(self.j.reason(path, 72), 'live process')

    def test_notes_at_nested_paths_survive_real_cleanup(self):
        path = self.directory('q1')
        nested = path/'nested'
        nested.mkdir()
        (nested/'NOTES-extra.md').write_text('keep')
        (nested/'old-output').write_text('remove')
        with self.guards():
            self.j.apply = True
            self.j.collect()
        self.assertTrue((nested/'NOTES-extra.md').is_file())
        self.assertFalse((nested/'old-output').exists())
        self.assertFalse((path/'output').exists())

    def test_probe_named_run_and_target_without_marker_are_kept(self):
        probe = self.directory('unbound-validation/runs/probe')
        target = self.directory('builds/targets/unknown')
        with self.guards():
            self.j.apply = True
            self.j.collect()
        self.assertTrue(probe.is_dir())
        self.assertTrue(target.is_dir())

    def test_readonly_directory_deleted_without_changing_shared_file_mode(self):
        path = self.directory('q1/old')
        shared = path/'output'
        live = self.root/'live-output'
        os.link(shared, live)
        shared.chmod(0o444)
        path.chmod(0o555)
        expected = path.stat().st_blocks*512
        with self.guards():
            self.j.apply = True
            self.assertTrue(self.j.remove(path, kind='task'))
        self.assertFalse(path.exists())
        self.assertEqual(live.stat().st_mode & 0o777, 0o444)
        self.assertEqual(self.j.reclaimable, expected)  # directory only, linked file remains

    def test_unreadable_reference_scan_aborts_before_removal(self):
        self.directory('q1')
        with self.guards(), patch.object(janitor, 'reference_paths', side_effect=OSError('unreadable')):
            with self.assertRaises(OSError):
                self.j.collect()
        self.assertEqual(self.j.removed, 0)

    def test_active_task_rechecked_before_delete(self):
        path = self.directory('q1/old')
        (self.cache/'agents/QUEUE.md').write_text('| Q1 | infra | new | active | worker |\n')
        with self.guards():
            self.j.apply = True
            self.assertFalse(self.j.remove(path, kind='task'))
        self.assertTrue(path.exists())

    def test_real_proc_open_file_is_discovered(self):
        path = self.root/'open-output'
        path.write_text('open')
        script = 'import sys; f=open(sys.argv[1]); print("ready", flush=True); sys.stdin.read()'
        process = subprocess.Popen([sys.executable, '-c', script, str(path)],
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
        try:
            self.assertEqual(process.stdout.readline().strip(), 'ready')
            self.assertIn(path, janitor.process_paths())
        finally:
            process.communicate(input='', timeout=5)

    def test_collector_does_not_count_its_own_open_descriptor(self):
        path = self.root/'collector-open-file'
        with path.open('w'):
            self.assertNotIn(path, janitor.process_paths())

    def test_orphan_target_removed_with_real_process_inspection(self):
        target = self.directory('builds/targets/orphan-real-proc')
        (target/'.unbound-source-root').write_text(str(self.root/'gone'))
        inspect = janitor.process_paths
        with self.guards(), patch.object(janitor, 'process_paths', side_effect=inspect):
            self.j.apply = True
            self.j.collect()
        self.assertFalse(target.exists())

    def test_symlink_created_after_discovery_keeps_run(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            (self.cache/'late-reference').symlink_to(run/'output')
            self.assertFalse(self.j.remove(run, kind='run', hours=72))
            self.j.apply = True
            self.assertFalse(self.j.remove(run, kind='run', hours=72))
        self.assertTrue(run.is_dir())
        self.assertTrue((self.cache/'late-reference').exists())

    def test_symlink_created_during_last_inventory_keeps_run(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        original = janitor.inventory
        calls = 0
        def concurrent_inventory(path):
            nonlocal calls
            calls += 1
            result = original(path)
            if calls == 3:
                (self.cache/'inventory-reference').symlink_to(run/'output')
            return result
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.j.apply = True
            with patch.object(janitor, 'inventory', side_effect=concurrent_inventory):
                self.assertFalse(self.j.remove(run, kind='run', hours=72))
        self.assertEqual(calls, 3)
        self.assertTrue((self.cache/'inventory-reference').exists())

    def test_symlink_created_during_permission_preparation_keeps_run(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        original = janitor.os.fwalk
        def concurrent_walk(*args, **kwargs):
            yield from original(*args, **kwargs)
            (self.cache/'permission-reference').symlink_to(run/'output')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.j.apply = True
            with patch.object(janitor.os, 'fwalk', side_effect=concurrent_walk):
                self.assertFalse(self.j.remove(run, kind='run', hours=72))
        self.assertTrue((self.cache/'permission-reference').exists())

    def test_new_nested_directory_and_moved_directory_links_are_seen(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        staging = self.root/'staging/nested'
        staging.mkdir(parents=True)
        (staging/'reference').symlink_to(run/'output')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            (self.root/'staging').rename(self.cache/'new-directory')
            self.j.apply = True
            self.assertFalse(self.j.remove(run, kind='run', hours=72))
        self.assertTrue(run.is_dir())

    def test_deleted_source_directory_can_be_replaced_with_new_link(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        source = self.cache/'source'
        source.mkdir()
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            source.rmdir()
            source.mkdir()
            (source/'late-reference').symlink_to(run/'output')
            self.j.apply = True
            self.assertFalse(self.j.remove(run, kind='run', hours=72))
        self.assertTrue(run.is_dir())

    def test_directory_changing_during_scan_is_retried_under_watch(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        (self.cache/'existing-reference').symlink_to(run/'output')
        watcher = janitor.ReferenceWatch()
        self.addCleanup(watcher.close)
        original = janitor.DirectoryIndex.entries
        changed = False
        def racing_entries(index, directory, info):
            nonlocal changed
            if directory == self.cache and not changed:
                changed = True
                (self.cache/'new-output').write_text('concurrent write')
                raise OSError('run-cache directory changed during indexing: fixture')
            return original(index, directory, info)
        with patch.object(janitor.DirectoryIndex, 'entries', racing_entries):
            references = janitor.reference_paths([self.cache], watcher=watcher)
        self.assertTrue(changed)
        self.assertIn(run/'output', references)

    def test_directory_removed_during_index_read_is_tolerated_under_watch(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        temporary = self.cache/'temporary-package'
        temporary.mkdir()
        (self.cache/'existing-reference').symlink_to(run/'output')
        watcher = janitor.ReferenceWatch()
        self.addCleanup(watcher.close)
        original = janitor.DirectoryIndex.entries
        removed = False
        def disappearing_entries(index, directory, info):
            nonlocal removed
            if directory == temporary and not removed:
                removed = True
                temporary.rmdir()
                raise FileNotFoundError(janitor.errno.ENOENT, 'concurrent cleanup', str(temporary))
            return original(index, directory, info)
        with patch.object(janitor.DirectoryIndex, 'entries', disappearing_entries):
            references = janitor.reference_paths([self.cache], watcher=watcher)
        self.assertTrue(removed)
        self.assertIn(run/'output', references)

    def test_reference_watch_overflow_fails_closed(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            overflow = janitor.struct.pack('iIII', -1, self.j.watch.OVERFLOW, 0, 0)
            self.j.apply = True
            with patch.object(janitor.os, 'read', return_value=overflow):
                with self.assertRaisesRegex(OSError, 'overflowed'):
                    self.j.remove(run, kind='run', hours=72)
        self.assertTrue(run.is_dir())
        self.assertEqual(self.j.removed, 0)

    def test_link_from_live_target_keeps_run_input(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        target = self.directory('builds/targets/live')
        (target/'.unbound-source-root').write_text(str(self.repo))
        (target/'input-link').symlink_to(run/'output')
        with self.guards():
            self.j.apply = True
            self.j.collect()
        self.assertTrue(run.is_dir())
        self.assertTrue(target.is_dir())

    def test_kache_dry_run_does_not_start_gc_and_real_gc_preserves_shared_outputs(self):
        cache = self.builds/'kache'
        cache.mkdir()
        result = subprocess.CompletedProcess([], 0, '{"bytes_held": 123}', '')
        with patch.object(janitor.subprocess, 'run', return_value=result) as run, \
             patch.object(janitor.shutil, 'which', return_value='/tools/kache'):
            self.j.kache_gc()
            run.assert_not_called()
            self.j.apply = True
            self.j.kache_gc()
        command = run.call_args.args[0]
        self.assertEqual(command[1:], ['gc', '--json'])
        env = run.call_args.kwargs['env']
        self.assertEqual(env['KACHE_CACHE_DIR'], str(cache))
        self.assertEqual(env['KACHE_MAX_SIZE'], '30GiB')
        self.assertEqual(env['KACHE_GC_EVICT_SHARED'], '0')

    def test_active_followup_protects_reused_cache_and_task_labelled_runs(self):
        reused = self.directory('q1/old')
        dated = self.directory('unbound-validation/runs/q2-20261101-old')
        with (self.cache/'agents/QUEUE.md').open('a') as stream:
            stream.write('| Q5 | infra | follow-up | active | notes ~/.cache/unbound/q1/NOTES.md |\n')
        with self.guards():
            self.j.apply = True
            self.j.collect()
        self.assertTrue(reused.is_dir())
        self.assertTrue(dated.is_dir())

    def test_runtime_dry_run_and_removal_guard_preserve_snapshot(self):
        cache = self.j.runtimes
        snapshot = cache/'snapshots'/('a'*64)
        snapshot.mkdir(parents=True)
        (snapshot/'unbound').write_text('snapshot')
        os.utime(snapshot, (0, 0))
        with patch.object(janitor.runtime_snapshot, 'active_snapshots', return_value=set()):
            removed = janitor.runtime_snapshot.cleanup(cache=cache, dry_run=True)
            self.assertEqual(removed, [snapshot.name])
            self.assertTrue(snapshot.is_dir())
            removed = janitor.runtime_snapshot.cleanup(cache=cache, remove=lambda _: False)
            self.assertEqual(removed, [])
            self.assertTrue(snapshot.is_dir())
            with patch.object(janitor.time, 'time', return_value=self.future):
                self.j.apply = True
                self.j.links = {snapshot/'unbound'}
                removed = janitor.runtime_snapshot.cleanup(cache=cache, remove=self.j.remove)
                self.assertEqual(removed, [])
                self.assertTrue(snapshot.is_dir())
                self.j.links = set()
                removed = janitor.runtime_snapshot.cleanup(cache=cache, remove=self.j.remove)
                self.assertEqual(removed, [snapshot.name])
                self.assertFalse(snapshot.exists())

    def test_before_gate_threshold_and_protection(self):
        with patch.object(janitor, 'free_space', return_value={'/': 1}), patch.object(janitor, 'main') as run:
            janitor.before_gate(self.repo)
        self.assertEqual(run.call_args.args[0], ['--repo', str(self.repo), '--apply', '--protect', str(self.repo)])
        with patch.object(janitor, 'free_space', return_value={'/': 1000*janitor.GIB}), patch.object(janitor, 'main') as run:
            janitor.before_gate(self.repo)
        run.assert_not_called()

    def test_timer_hourly_and_preserves_cache_locations(self):
        units = install_janitor.units(self.repo, dict(os.environ))
        self.assertIn('OnCalendar=hourly', units['unbound-janitor.timer'])
        self.assertIn('Persistent=true', units['unbound-janitor.timer'])
        self.assertIn(str(self.builds), units['unbound-janitor.service'])
        self.assertIn('--apply --repo', units['unbound-janitor.service'])
        self.assertIn('WorkingDirectory='+str(self.repo)+'\n', units['unbound-janitor.service'])


if __name__ == '__main__':
    unittest.main()
