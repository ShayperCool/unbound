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
import threading
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
        self.output = io.StringIO()
        self.silence = contextlib.redirect_stdout(self.output)
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

    def movable_relative_link(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        source = self.cache/'old-parent/tree'
        source.mkdir(parents=True)
        harmless = self.cache/run.name/'output'
        harmless.parent.mkdir()
        harmless.write_text('unrelated target')
        (source/'relative-link').symlink_to(Path('../..')/run.name/'output')
        destination = run.parent/'retained/tree'
        destination.parent.mkdir()
        return run, source, destination

    def test_directory_move_resolves_existing_relative_links_in_new_context(self):
        run, source, destination = self.movable_relative_link()
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.assertNotIn(run/'output', self.j.links)
            source.rename(destination)
            self.j.refresh_links()
            self.assertIn(run/'output', self.j.links)
            self.assertEqual(self.j.protection_reason(run), 'symlink reference')

    def test_directory_move_during_initial_scan_rescans_relative_links(self):
        run, source, destination = self.movable_relative_link()
        watcher = janitor.ReferenceWatch()
        self.addCleanup(watcher.close)
        watcher.add(source.parent)
        watcher.add(destination.parent)
        original = janitor.DirectoryIndex.entries
        moved = False
        def moving_entries(index, directory, info):
            nonlocal moved
            entries = original(index, directory, info)
            if directory == source and not moved:
                moved = True
                source.rename(destination)
            return entries
        with patch.object(janitor.DirectoryIndex, 'entries', moving_entries):
            references = janitor.reference_paths([self.cache, self.volume], watcher=watcher)
        self.assertTrue(moved)
        self.assertIn(run/'output', references)

    def test_directory_move_updates_descendant_watch_paths(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        source = self.cache/'source'
        (source/'nested').mkdir(parents=True)
        destination = self.cache/'destination'
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            source.rename(destination)
            self.j.refresh_links()
            (destination/'nested/new-reference').symlink_to(run/'output')
            self.j.apply = True
            self.assertFalse(self.j.remove(run, kind='run', hours=72))
        self.assertTrue(run.is_dir())

    def test_multiple_directory_moves_before_poll_rescan_final_location(self):
        run, source, destination = self.movable_relative_link()
        intermediate = self.cache/'intermediate'
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            source.rename(intermediate)
            intermediate.rename(destination)
            self.j.refresh_links()
            self.assertIn(run/'output', self.j.links)

    def test_directory_moved_then_deleted_before_poll_does_not_block_scan(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        source = self.cache/'source'
        source.mkdir()
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            destination = self.cache/'temporary'
            source.rename(destination)
            destination.rmdir()
            self.j.refresh_links()
            self.assertTrue(self.j.remove(run, kind='run', hours=72))
        self.assertTrue(run.is_dir())

    def test_directory_move_outside_watched_roots_defers_cleanup(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        source = self.cache/'source'
        source.mkdir()
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            source.rename(self.root/'outside-watched-roots')
            self.j.apply = True
            with self.assertRaisesRegex(OSError, 'reference directory moved'):
                self.j.remove(run, kind='run', hours=72)
        self.assertTrue(run.is_dir())

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

    def promote_fixture(self, work, **metadata):
        baseline = self.cache/'agents/baseline'
        baseline.mkdir(parents=True, exist_ok=True)
        (baseline/'summary.json').write_text(json.dumps(dict(
            label='sheriff-S20-example', work=str(work), **metadata)))

    def test_promoted_suite_and_all_declared_references_are_not_candidates(self):
        work = self.directory('q1/sheriff-S20-example')
        linked = self.directory('q1/linked-output')
        declared = self.directory('q1/json-output')
        command = self.directory('q1/command output')
        printed = self.directory('q1/printed output')
        unpromoted = self.directory('q1/sheriff-S21-unpromoted')
        (work/'link').symlink_to(linked)
        (work/'metadata.json').write_text(json.dumps({'artifact': str(declared)}))
        (work/'step.log').write_text('$ tool --work '+janitor.shlex.quote(str(command))+'\nunbound exit 0 '+janitor.shlex.quote(str(printed/'output'))+'\n')
        self.promote_fixture(work)
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            candidates = {path for path, _, _ in self.j.candidates()}
            for protected in (work, linked, declared, command, printed):
                self.assertNotIn(protected, candidates)
                self.j.apply = True
                self.assertFalse(self.j.remove(protected, kind='task'))
            self.assertIn(unpromoted, candidates)
            self.assertTrue(self.j.remove(unpromoted, kind='task'))
        self.assertTrue((linked/'output').exists())

    def test_late_promotion_and_metadata_edit_protect_new_references(self):
        work = self.directory('q1/sheriff-S20-example')
        first = self.directory('q1/first')
        second = self.directory('q1/second')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.promote_fixture(work, artifact=str(first))
            self.j.apply = True
            self.assertFalse(self.j.remove(first, kind='task'))
            (work/'later.json').write_text(json.dumps({'artifact': str(second)}))
            self.assertFalse(self.j.remove(second, kind='task'))
        self.assertTrue(second.exists())

    def test_shell_bodies_and_equals_in_paths_are_protected(self):
        work = self.directory('q1/sheriff-S20-example')
        metadata = self.directory('q1/metadata=name')
        logged = self.directory('q1/logged=name')
        direct = self.directory('q1/direct=name')
        def shell(path, executable):
            return janitor.shlex.join(['bash', '-c', janitor.shlex.join([executable, '--work', str(path)])])
        self.promote_fixture(work, command=shell(metadata, 'tool'))
        (work/'step.log').write_text('$ '+shell(logged, '/usr/bin/tool')+'\n')
        (work/'metadata.json').write_text(json.dumps({'command': 'tool --work='+str(direct)}))
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            candidates = {path for path, _, _ in self.j.candidates()}
            for path in (metadata, logged, direct):
                self.assertNotIn(path, candidates)
                self.j.apply = True
                self.assertFalse(self.j.remove(path, kind='task'))
        self.assertEqual(list(janitor.command_paths('tool '+str(direct))), [direct])

    def test_shell_redirections_operators_and_quoted_punctuation_keep_references(self):
        work = self.directory('q1/sheriff-S20-example')
        redirected = self.directory('q1/redirected')
        absolute = self.directory('q1/absolute')
        adjacent = self.directory('q1/adjacent')
        quoted = self.directory('q1/quoted&&name')
        shell = lambda body: janitor.shlex.join(['bash', '-c', body])
        self.promote_fixture(work, command=shell('cat<'+str(redirected/'output')))
        (work/'step.log').write_text('$ '+shell('tool --work='+str(adjacent)+'&&true')+'\n'+
                                    '$ '+shell('/usr/bin/cat<'+str(absolute/'output'))+'\n')
        (work/'metadata.json').write_text(json.dumps({'command': shell(
            'tool --work='+janitor.shlex.quote(str(quoted)))}))
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            candidates = {path for path, _, _ in self.j.candidates()}
            for path in (redirected, adjacent, quoted, absolute):
                self.assertNotIn(path, candidates)
                self.j.apply = True
                self.assertFalse(self.j.remove(path, kind='task'))

    def test_shortened_command_and_human_summary_do_not_protect_cache_ancestors(self):
        value = {'command': 'tool '+('x'*389)+' /home', 'summary': '3/10 values; /home'}
        self.assertEqual(len(value['command']), 400)
        self.assertEqual(list(janitor.suite_references(value)), [])

    def test_continuous_directory_churn_finishes_with_logged_batch_progress(self):
        root = self.directory('q1')
        for n in range(96):
            (root/f'old-{n:03d}').write_text('old')
        keep = root/'retained'
        keep.write_text('reference')
        moving = self.directory('moving')
        (moving/'link').symlink_to(keep)
        outside = self.root/'outside'
        start, moved, stop = threading.Event(), threading.Event(), threading.Event()
        def churn():
            start.wait()
            while not stop.is_set():
                moving.rename(outside)
                moved.set()
                time.sleep(0.002)
                outside.rename(moving)
                time.sleep(0.002)
        thread = threading.Thread(target=churn)
        thread.start()
        emit = self.j.emit
        def progress(action, **fields):
            emit(action, **fields)
            if action == 'batch_finish' and fields['batch'] == 1:
                start.set()
                self.assertTrue(moved.wait(2))
        began = time.monotonic()
        try:
            with self.guards(), patch.object(self.j, 'emit', side_effect=progress):
                self.j.apply = True
                self.j.run_seconds = 0.5
                self.j.collect()
        finally:
            stop.set()
            start.set()
            thread.join(2)
        self.assertFalse(thread.is_alive())
        self.assertLess(time.monotonic()-began, 5)
        self.assertTrue(self.j.partial or self.j.removed == 97)
        self.assertGreaterEqual(self.j.removed, 32)
        self.assertTrue(keep.exists())
        rows = [json.loads(line) for line in self.output.getvalue().splitlines()]
        batches = [row for row in rows if row['action'] == 'batch_finish']
        if self.j.partial:
            self.assertTrue(any(row['status'] == 'deferred' for row in batches))
        self.assertEqual(sum(row['removals'] for row in batches), self.j.removed)
        self.assertTrue(all(row['candidates'] <= janitor.REMOVAL_BATCH_SIZE for row in rows
                            if row['action'] == 'batch_start'))

    def test_failed_batch_recovers_before_later_batches(self):
        root = self.directory('q1')
        for n in range(96):
            (root/f'old-{n:03d}').write_text('old')
        original = self.j.remove_batch
        calls = 0
        def invalidated(candidates):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise TimeoutError('batch discovery budget')
            return original(candidates)
        with self.guards(), patch.object(self.j, 'remove_batch', side_effect=invalidated):
            self.j.apply = True
            self.j.collect()
        self.assertTrue(self.j.partial)
        self.assertEqual(self.j.discovery_attempts, 2)
        self.assertEqual(self.j.removed, 65)
        self.assertFalse(self.j.stopped)

    def test_persistent_discovery_timeouts_are_bounded_and_still_run_cache_gc(self):
        with self.guards(), patch.object(self.j, 'discover', side_effect=TimeoutError('discovery budget')) as discover, \
             patch.object(self.j, 'kache_gc') as gc:
            self.j.collect()
        self.assertEqual(discover.call_count, 1+janitor.MAX_REDISCOVERIES)
        self.assertTrue(self.j.partial)
        self.assertTrue(self.j.stopped)
        self.assertEqual(self.j.removed, 0)
        gc.assert_called_once()

    def test_unpaired_move_inside_roots_reconciles_new_references(self):
        target = self.directory('q1/target')
        unrelated = self.directory('q1/unrelated')
        moving = self.directory('moving')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            (moving/'link').symlink_to(target/'output')
            parent = self.volume/'new-parent'
            parent.mkdir()
            moving.rename(parent/'moved')
            self.j.apply = True
            self.assertFalse(self.j.remove(target, kind='task'))
            self.assertTrue(self.j.remove(unrelated, kind='task'))
        self.assertTrue(target.exists())

    def test_new_reference_moved_outside_roots_stops_later_deletions(self):
        root = self.directory('q1')
        for n in range(96):
            (root/f'old-{n:03d}').write_text('old')
        target = root/'z-target'
        target.write_text('keep')
        moving = self.directory('moving')
        outside = self.root/'outside'
        emit = self.j.emit
        def move_new_link(action, **fields):
            emit(action, **fields)
            if action == 'batch_finish' and fields['batch'] == 1:
                (moving/'link').symlink_to(target)
                moving.rename(outside)
        with self.guards(), patch.object(self.j, 'emit', side_effect=move_new_link):
            self.j.apply = True
            self.j.collect()
        self.assertTrue(self.j.partial)
        self.assertTrue(self.j.stopped)
        self.assertEqual(self.j.removed, 32)
        self.assertTrue((outside/'link').exists())
        self.assertEqual(target.read_text(), 'keep')

    def test_watch_retains_discovered_destinations_across_retry(self):
        target = self.directory('q1/target')
        link = self.cache/'reference'
        link.symlink_to(target/'output')
        watch = janitor.ReferenceWatch()
        self.addCleanup(watch.close)
        first = janitor.reference_paths([self.cache], watcher=watch, index_root=self.cache)
        self.assertIn(target/'output', first)
        link.unlink()
        retried = janitor.reference_paths([self.cache], watcher=watch, index_root=self.cache)
        self.assertIn(target/'output', retried)

    def test_new_symlink_moved_outside_roots_defers_removals(self):
        target = self.directory('q1/target')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            link = self.volume/'new-link'
            link.symlink_to(target/'output')
            link.rename(self.root/'outside-link')
            self.j.apply = True
            with self.assertRaises(janitor.ReferenceChanged):
                self.j.remove(target, kind='task')
        self.assertTrue(target.exists())

    def test_run_budget_returns_explicit_partial_summary(self):
        with self.guards():
            code = janitor.main(['--repo', str(self.repo), '--dry-run', '--run-seconds', '0.000001'])
        self.assertEqual(code, 3)
        logs = list((self.cache/'agents/janitor').glob('*.jsonl'))
        row = json.loads(logs[0].read_text().splitlines()[-1])
        self.assertEqual(row['action'], 'summary')
        self.assertTrue(row['partial'])
        self.assertFalse(row['failed'])
        self.assertEqual(row['removals'], 0)
        self.assertIn('SuccessExitStatus=3', install_janitor.units(self.repo, {})['unbound-janitor.service'])

    def test_before_gate_threshold_and_protection(self):
        for free in (1, 19*janitor.GIB):
            with self.subTest(free=free), patch.dict(os.environ, {'UNBOUND_JANITOR_MIN_FREE_GIB': '20'}), \
                 patch.object(janitor, 'free_space', return_value={'/': free}), \
                 patch.object(janitor.subprocess, 'run') as run, patch.object(janitor, 'main') as collect:
                run.return_value.returncode = 0
                janitor.before_gate(self.repo)
                self.assertEqual(run.call_args.args[0],
                                 ['systemctl', '--user', 'start', '--no-block', 'unbound-janitor.service'])
                self.assertEqual(run.call_args.kwargs['timeout'], janitor.GATE_HANDOFF_SECONDS)
                collect.assert_not_called()
        with patch.object(janitor, 'free_space', return_value={'/': 1000*janitor.GIB}), \
             patch.object(janitor.subprocess, 'run') as run:
            janitor.before_gate(self.repo)
        run.assert_not_called()

    def test_before_gate_large_backlog_has_bounded_handoff(self):
        # A real slow service-manager substitute must be killed by the timeout.
        for n in range(1000):
            (self.directory('q1') if n == 0 else self.volume/'q1').joinpath(str(n)).touch()
        original = subprocess.run
        def stalled_manager(command, **kwargs):
            return original([sys.executable, '-c', 'import time; time.sleep(30)'], **kwargs)
        with patch.object(janitor, 'free_space', return_value={'/': 1}), \
             patch.object(janitor.subprocess, 'run', side_effect=stalled_manager), \
             patch.object(janitor.Janitor, 'collect', side_effect=AssertionError('inline cleanup')):
            start = time.monotonic()
            janitor.before_gate(self.repo)
            self.assertLess(time.monotonic()-start, janitor.GATE_HANDOFF_SECONDS+1)

    def test_batch_refreshes_expensive_guards_once_after_all_preparation(self):
        root = self.directory('q1')
        paths = [root/str(n) for n in range(32)]
        for path in paths:
            path.write_text('old')
        late = paths[-1]
        with self.guards(), patch.object(janitor, 'process_paths', return_value={late}) as processes:
            self.j.apply = True
            self.assertEqual(self.j.remove_batch([(p, 'task', 24) for p in paths]), 31)
        self.assertEqual(processes.call_count, 1)
        self.assertTrue(late.exists())

    def test_real_clock_old_directory_survives_permission_preparation(self):
        path = self.directory('q1/old')
        path.chmod(0o555)
        self.j.apply = True
        self.j.idle_hours = 0.0001
        time.sleep(0.5)
        with patch.object(janitor, 'process_paths', return_value=set()):
            self.assertTrue(self.j.remove(path, kind='task'))
        self.assertFalse(path.exists())

    def test_write_during_directory_permission_preparation_is_kept(self):
        path = self.directory('q1/old')
        path.chmod(0o555)
        original = janitor.os.fchmod
        def concurrent_write(descriptor, mode):
            original(descriptor, mode)
            (path/'output').write_text('new output')
        self.j.apply = True
        self.j.idle_hours = 0.0001
        time.sleep(0.5)
        with patch.object(janitor, 'process_paths', return_value=set()), \
             patch.object(janitor.os, 'fchmod', side_effect=concurrent_write):
            self.assertFalse(self.j.remove(path, kind='task'))
        self.assertEqual((path/'output').read_text(), 'new output')

    def test_write_during_other_file_preparation_is_kept(self):
        root = self.directory('q1')
        paths = [root/'first', root/'second']
        for path in paths:
            path.write_text('old')
        original = janitor.inventory
        def concurrent_inventory(path):
            result = original(path)
            if path == paths[1]:
                paths[0].write_text('new output')
            return result
        self.j.apply = True
        self.j.idle_hours = 0.0001
        time.sleep(0.5)
        with patch.object(janitor, 'process_paths', return_value=set()), \
             patch.object(janitor, 'inventory', side_effect=concurrent_inventory):
            self.assertEqual(self.j.remove_batch([(p, 'task', None) for p in paths]), 1)
        self.assertEqual(paths[0].read_text(), 'new output')

    def test_batch_rechecks_queue_and_links_between_removals(self):
        paths = [self.directory(f'q1/old-{n}') for n in range(3)]
        original = janitor.shutil.rmtree
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.j.apply = True
            def remove_first(path):
                original(path)
                (self.cache/'late-reference').symlink_to(paths[1]/'output')
                (self.cache/'agents/QUEUE.md').write_text('| Q1 | infra | now | active | worker |\n')
            with patch.object(janitor.shutil, 'rmtree', side_effect=remove_first):
                self.assertEqual(self.j.remove_batch([(p, 'task', 24) for p in paths]), 1)
        self.assertTrue(paths[1].exists())
        self.assertTrue(paths[2].exists())

    def test_slow_removal_refreshes_process_guards_for_next_entry(self):
        paths = [self.directory(f'q1/old-{n}') for n in range(2)]
        original = janitor.shutil.rmtree
        def slow_removal(path):
            original(path)
            time.sleep(janitor.GUARD_REUSE_SECONDS+0.01)
        with self.guards(), patch.object(janitor, 'process_paths', side_effect=[set(), {paths[1]/'output'}]) as processes, \
             patch.object(janitor.shutil, 'rmtree', side_effect=slow_removal):
            self.j.apply = True
            self.assertEqual(self.j.remove_batch([(p, 'task', 24) for p in paths]), 1)
        self.assertEqual(processes.call_count, 2)
        self.assertTrue(paths[1].exists())

    def test_timer_hourly_and_preserves_cache_locations(self):
        units = install_janitor.units(self.repo, dict(os.environ))
        self.assertIn('OnCalendar=hourly', units['unbound-janitor.timer'])
        self.assertIn('Persistent=true', units['unbound-janitor.timer'])
        self.assertIn(str(self.builds), units['unbound-janitor.service'])
        self.assertIn('--apply --repo', units['unbound-janitor.service'])
        self.assertIn('WorkingDirectory='+str(self.repo)+'\n', units['unbound-janitor.service'])


if __name__ == '__main__':
    unittest.main()
