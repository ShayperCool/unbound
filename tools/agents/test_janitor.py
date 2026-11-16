#!/usr/bin/env python3
"""Disk janitor safety controls, using private fixtures only."""
import contextlib
import io
import json
import shutil
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

    def instances(self, probe, names):
        """Instances of runs/<probe>/ in age order (the last is the newest)."""
        self.aged = [self.directory(f'unbound-validation/runs/{probe}/{name}') for name in names]
        return self.aged

    def collect_applied(self, processes=()):
        # Instance ages are set last: adding content inside an instance changes its time.
        for age, path in enumerate(getattr(self, 'aged', [])):
            os.utime(path, (1_000_000+age, 1_000_000+age))
        with self.guards(), patch.object(janitor, 'process_paths', return_value=set(processes)):
            self.j.apply = True
            self.j.collect()

    def test_old_instances_of_a_probe_are_removed_except_the_newest(self):
        old, older, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        self.collect_applied()
        self.assertFalse(old.exists())
        self.assertFalse(older.exists())
        self.assertTrue(newest.is_dir())

    def test_a_link_inside_a_removed_run_protects_nothing_and_its_inside_is_not_walked(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (first/'deep'/'er').mkdir(parents=True)
        (first/'deep'/'er'/'link').symlink_to(second/'output')
        walked = []
        original = janitor.ReferenceWatch.add
        def add(watch, directory):
            walked.append(os.fspath(directory))
            return original(watch, directory)
        with patch.object(janitor.ReferenceWatch, 'add', add):
            self.collect_applied()
        self.assertFalse(first.exists())
        self.assertFalse(second.exists())
        self.assertTrue(newest.is_dir())
        self.assertNotIn(str(first/'deep'), walked)
        self.assertIn(str(newest), walked)

    def test_a_run_protected_from_outside_keeps_what_its_own_links_name(self):
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        (self.cache/'retained').symlink_to(second/'output')
        (second/'link').symlink_to(first/'output')
        self.collect_applied()
        self.assertTrue(second.is_dir())
        self.assertTrue(first.is_dir())  # named from inside a kept run
        self.assertFalse(third.exists())
        self.assertIn('"unseal"', self.output.getvalue())

    def test_a_run_in_live_use_keeps_what_its_own_links_name(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (second/'link').symlink_to(first/'output')
        self.collect_applied(processes=[second/'output'])
        self.assertTrue(second.is_dir())
        self.assertTrue(first.is_dir())

    def test_a_recently_written_run_keeps_what_its_own_links_name(self):
        # Old by its own timestamps but written inside lately: kept, and walked from then on.
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        (first/'link').symlink_to(third/'output')
        os.utime(first/'output', (self.future, self.future))
        self.collect_applied()
        self.assertTrue(first.is_dir())
        self.assertTrue(third.is_dir())
        self.assertFalse(second.exists())

    def test_runs_named_by_a_sheriff_suite_are_kept(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        suite = self.volume/'unbound-validation/suite/sheriff-S9-abc'
        (suite/'probe').mkdir(parents=True)
        (suite/'probe'/'run.log').write_text(f'$ python3 run.py --out {second}/output\n')
        self.collect_applied()
        self.assertTrue(second.is_dir())
        self.assertFalse(first.exists())
        known = json.loads((self.cache/'agents/janitor/suite-references.json').read_text())
        self.assertEqual(known['format'], janitor.SUITE_REFERENCE_FORMAT)
        self.assertEqual(known['files'][str(suite/'probe'/'run.log')][4], [f'{second}/output'])

    def test_runs_named_through_a_linked_prefix_are_kept(self):
        # Suites write ~/.cache/unbound/validation/runs/...; validation links to the volume's.
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (self.cache/'validation').symlink_to(self.volume/'unbound-validation')
        suite = self.cache/'agents/suite/sheriff-S9-abc'
        suite.mkdir(parents=True)
        (suite/'summary.json').write_text(json.dumps(
            {'command': f'python3 run.py --out {self.cache}/validation/runs/probe/gate-2/output'}))
        self.collect_applied()
        self.assertTrue(second.is_dir())
        self.assertFalse(first.exists())

    def test_references_cached_in_an_earlier_format_are_read_again(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        suite = self.volume/'unbound-validation/suite/sheriff-S9-abc'
        suite.mkdir(parents=True)
        log = suite/'run.log'
        log.write_text(f'{second}/output\n')
        info = log.stat()
        state = self.cache/'agents/janitor/suite-references.json'
        state.parent.mkdir(parents=True)
        # An earlier format, with this file's identity, claiming it names nothing.
        state.write_text(json.dumps({'format': janitor.SUITE_REFERENCE_FORMAT-1, 'files': {
            str(log): [info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns, []]}}))
        self.collect_applied()
        self.assertTrue(second.is_dir())

    def test_a_recently_written_run_protects_older_runs_it_names(self):
        # The run that names another is newer, so a removal in age order would reach the named
        # run first: every sealed run is checked for recent writes before any run is removed.
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        (third/'link').symlink_to(first/'output')
        os.utime(third/'output', (self.future, self.future))
        self.collect_applied()
        self.assertTrue(third.is_dir())
        self.assertTrue(first.is_dir())
        self.assertFalse(second.exists())
        self.assertNotIn('links into a removed run', self.output.getvalue())

    def test_no_run_is_removed_before_every_sealed_run_is_checked(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        # The run budget ends after the first sealed run has been checked.
        original = janitor.inventory
        checked = []
        def inventory(path, *args, **kwargs):
            if not checked:
                self.j.deadline = time.monotonic()
            checked.append(path)
            return original(path, *args, **kwargs)
        with patch.object(janitor, 'inventory', inventory):
            self.collect_applied()
        self.assertEqual(len(checked), 1)
        self.assertTrue(first.is_dir())
        self.assertTrue(second.is_dir())
        self.assertIn('sealed runs not all checked for recent writes', self.output.getvalue())
        self.assertTrue(self.j.partial)

    def check_fails_on_recent_run(self, error):
        """Runs gate-1..4 and a landed task q1 that only gate-1 (old, sealed) links to; gate-3 was
        written inside lately and its walk at verification fails with error (None: succeeds)."""
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        task = self.directory('q1')
        (first/'link').symlink_to(task/'output')
        (third/'link').symlink_to(second/'output')
        os.utime(third/'output', (self.future, self.future))
        original = janitor.reference_paths
        def reference_paths(paths, *args, **kwargs):
            if error and list(paths) == [third]:
                raise error
            return original(paths, *args, **kwargs)
        for age, path in enumerate(self.aged):
            os.utime(path, (1_000_000+age, 1_000_000+age))
        with self.guards(), patch.object(janitor, 'reference_paths', reference_paths):
            self.j.apply = True
            self.j.collect()
            cleanup, gc = janitor.runtime_snapshot.cleanup, janitor.Janitor.kache_gc
            return first, second, task, cleanup.called, gc.called

    def assert_nothing_removed(self, outcome):
        # The links inside unchecked sealed runs were never read, so nothing else may go either.
        first, second, task, cleaned, collected = outcome
        self.assertTrue(first.is_dir())
        self.assertTrue(second.is_dir())
        self.assertTrue((task/'output').is_file())
        self.assertEqual(self.j.removed, 0)
        self.assertTrue(self.j.partial)
        self.assertFalse(cleaned)  # no runtime cleanup
        self.assertTrue(collected)  # kache gc still runs
        self.assertIn('"batch_limit"', self.output.getvalue())

    def test_a_timed_out_check_of_sealed_runs_removes_nothing(self):
        self.assert_nothing_removed(self.check_fails_on_recent_run(TimeoutError('janitor run budget exhausted')))
        self.assertIn('sealed runs not all checked for recent writes', self.output.getvalue())

    def test_a_sealed_run_that_cannot_be_walked_stops_removals(self):
        self.assert_nothing_removed(self.check_fails_on_recent_run(janitor.ReferenceChanged('moved')))
        self.assertIn('sealed run could not be walked: moved', self.output.getvalue())

    def test_a_checked_sealed_run_takes_its_links_with_it(self):
        # Control: once every sealed run is checked, an old run and what only it named go.
        first, second, task, cleaned, collected = self.check_fails_on_recent_run(None)
        self.assertFalse(first.exists())
        self.assertFalse((task/'output').exists())  # a task's contents go
        self.assertTrue(second.is_dir())  # named from the recently written run
        self.assertFalse(self.j.partial)
        self.assertTrue(cleaned)

    def test_a_run_whose_files_change_during_its_check_keeps_what_it_names(self):
        # A file inside gate-3 goes while it is checked: gate-3 is being written to, not gone.
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        (third/'link').symlink_to(first/'output')
        original = janitor.inventory
        def inventory(path, *args, **kwargs):
            if path == third and not getattr(inventory, 'raised', False):
                inventory.raised = True
                raise FileNotFoundError(2, 'No such file or directory', str(third/'temporary'))
            return original(path, *args, **kwargs)
        with patch.object(janitor, 'inventory', inventory):
            self.collect_applied()
        self.assertTrue(first.is_dir())  # named from gate-3, which was walked
        self.assertFalse(second.exists())
        self.assertIn('"unseal"', self.output.getvalue())
        self.assertNotIn('links into a removed run', self.output.getvalue())

    def test_runs_sealed_again_by_a_rediscovery_are_checked_before_removals(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        task = self.directory('q1')
        (first/'link').symlink_to(task/'output')
        original = janitor.Janitor.remove_batch
        calls = []
        def remove_batch(janitor_, candidates):
            calls.append(candidates)
            if len(calls) == 1:
                # gate-1 is written to while its batch runs out of time; it is sealed again.
                os.utime(first/'output', (self.future, self.future))
                raise TimeoutError('janitor scan budget exhausted')
            return original(janitor_, candidates)
        with patch.object(janitor, 'REMOVAL_BATCH_SIZE', 1), \
             patch.object(janitor.Janitor, 'remove_batch', remove_batch):
            self.collect_applied()
        self.assertEqual(self.output.getvalue().count('"discovery_start"'), 2)
        self.assertTrue(first.is_dir())
        self.assertTrue((task/'output').is_file())  # named from gate-1, walked after the rediscovery
        self.assertFalse(second.exists())

    def test_a_suite_path_through_a_link_inside_a_run_keeps_both_runs(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (first/'latest').symlink_to(second)
        suite = self.volume/'unbound-validation/suite/sheriff-S9-abc'
        suite.mkdir(parents=True)
        (suite/'run.log').write_text(f'{first}/latest/output\n')
        self.collect_applied()
        self.assertTrue(first.is_dir())
        self.assertTrue(second.is_dir())

    def test_a_run_named_at_the_end_of_a_sentence_is_kept(self):
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        suite = self.volume/'unbound-validation/suite/sheriff-S9-abc'
        suite.mkdir(parents=True)
        (suite/'run.log').write_text(f'kept {second}.\nreused {third}: yes\n')
        self.collect_applied()
        self.assertTrue(second.is_dir())
        self.assertTrue(third.is_dir())
        self.assertFalse(first.exists())

    def test_a_suite_path_through_links_after_another_runs_folder_keeps_both_runs(self):
        # The path passes a runs/ folder that is not the validation runs before the linked one.
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (first/'latest').symlink_to(second)
        (self.cache/'runs/x').mkdir(parents=True)
        (self.cache/'runs/x/validation').symlink_to(self.volume/'unbound-validation')
        suite = self.volume/'unbound-validation/suite/sheriff-S9-abc'
        suite.mkdir(parents=True)
        (suite/'run.log').write_text(f'{self.cache}/runs/x/validation/runs/probe/gate-1/latest/output\n')
        self.collect_applied()
        self.assertTrue(first.is_dir())
        self.assertTrue(second.is_dir())

    def test_folders_without_numbers_beside_runs_and_single_runs_stay(self):
        first, second = self.instances('probe', ['gate-1', 'gate-2'])
        logs = self.directory('unbound-validation/runs/probe/logs')
        single = self.directory('unbound-validation/runs/a-single-run')
        (single/'capture').mkdir()
        (single/'command.json').write_text('{}')
        os.utime(logs, (2_000_000, 2_000_000))  # newer than every run of the probe
        self.collect_applied()
        self.assertTrue(logs.is_dir())
        self.assertTrue(second.is_dir())  # the newest run, not the newer logs folder
        self.assertFalse(first.exists())
        self.assertTrue((single/'capture').is_dir())

    def test_no_run_is_removed_until_every_sheriff_suite_file_is_read(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        suite = self.volume/'unbound-validation/suite/sheriff-S9-abc'
        suite.mkdir(parents=True)
        (suite/'summary.json').write_text('{}')
        with patch.object(janitor, 'SUITE_REFERENCE_SECONDS', 0):
            self.collect_applied()
        self.assertTrue(first.is_dir())
        self.assertTrue(second.is_dir())
        self.assertIn('sheriff suite references not read yet', self.output.getvalue())
        self.assertTrue(self.j.partial)

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
            if Path(directory) == self.cache and not changed:
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
            if Path(directory) == temporary and not removed:
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
            if Path(directory) == source and not moved:
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

    def test_listed_directory_moved_outside_roots_does_not_block_cleanup(self):
        # Its contents were listed while watched, so nothing unseen left the view.
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        source = self.cache/'source'
        source.mkdir()
        (source/'existing.txt').write_text('listed')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            source.rename(self.root/'outside-watched-roots')
            self.j.apply = True
            self.assertTrue(self.j.remove(run, kind='run', hours=72))
        self.assertFalse(run.exists())

    def test_known_link_moved_outside_roots_keeps_its_target_and_cleanup_continues(self):
        target = self.directory('q1/target')
        other = self.directory('q1/other')
        (self.cache/'reference').symlink_to(target/'output')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            (self.cache/'reference').rename(self.root/'outside-reference')
            self.j.apply = True
            self.assertFalse(self.j.remove(target, kind='task'))
            self.assertTrue(self.j.remove(other, kind='task'))
        self.assertTrue(target.exists())

    def test_inspected_new_file_moved_outside_roots_does_not_block_cleanup(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            (self.cache/'temporary').write_text('regular file')
            self.j.refresh_links()  # poll() sees a regular file
            (self.cache/'temporary').rename(self.root/'outside-file')
            self.j.apply = True
            self.assertTrue(self.j.remove(run, kind='run', hours=72))

    def test_uninspected_new_file_moved_outside_roots_defers_cleanup(self):
        # Created and moved out before any poll: it could have been a link.
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            (self.cache/'temporary').write_text('regular file')
            (self.cache/'temporary').rename(self.root/'outside-file')
            self.j.apply = True
            with self.assertRaisesRegex(OSError, 'for example: .*temporary'):
                self.j.remove(run, kind='run', hours=72)
        self.assertTrue(run.is_dir())

    def test_new_directory_moved_outside_before_inspection_defers_cleanup(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            (self.cache/'fresh').mkdir()
            (self.cache/'fresh').rename(self.root/'outside-fresh')
            self.j.apply = True
            with self.assertRaises(janitor.ReferenceChanged):
                self.j.remove(run, kind='run', hours=72)
        self.assertTrue(run.is_dir())

    def assert_target_kept(self, target, act):
        """act() runs after discovery; the target must survive, by a keep or a deferral."""
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            act()
            self.j.apply = True
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def test_a_path_recreated_after_a_settled_move_is_watched_again(self):
        target = self.directory('q1/target')
        work = self.cache/'work'
        (work/'x').mkdir(parents=True)
        def act():
            (work/'x').rename(self.root/'outside-x')
            self.j.refresh_links()
            (work/'x').mkdir()
            self.j.refresh_links()
            (work/'x'/'link').symlink_to(target/'output')
        self.assert_target_kept(target, act)

    def test_a_directory_moved_back_after_a_settled_move_is_watched_again(self):
        target = self.directory('q1/target')
        work = self.cache/'work'
        (work/'x').mkdir(parents=True)
        def act():
            (work/'x').rename(self.root/'outside-x')
            self.j.refresh_links()
            (self.root/'outside-x').rename(work/'x')
            self.j.refresh_links()
            (work/'x'/'sub').mkdir()
            (work/'x'/'sub'/'link').symlink_to(target/'output')
        self.assert_target_kept(target, act)

    def test_unverified_names_follow_a_renamed_directory(self):
        target = self.directory('q1/target')
        holder = self.cache/'d'
        holder.mkdir()
        def act():
            (holder/'n').symlink_to(target/'output')
            holder.rename(self.cache/'d2')
            (self.cache/'d2'/'n').rename(self.root/'outside-n')
        self.assert_target_kept(target, act)

    def test_a_new_directory_moved_out_before_its_walk_defers(self):
        target = self.directory('q1/target')
        fresh = self.cache/'x'
        def act():
            fresh.mkdir()
            (fresh/'l').symlink_to(target/'output')
            self.j.watch.poll()  # sees a directory, not yet walked
            fresh.rename(self.root/'outside-x')
        self.assert_target_kept(target, act)

    def link_target_outside_roots_replaced(self, replace):
        target = self.directory('q1/target')
        outside = self.root/'ext'/'T'
        outside.mkdir(parents=True)
        (self.cache/'l').symlink_to(outside)
        def act():
            if replace == 'move':
                outside.rename(outside.parent/'T.old')
            else:
                outside.rmdir()
            outside.mkdir()
            (outside/'link').symlink_to(target/'output')
        self.assert_target_kept(target, act)

    def test_a_link_target_outside_the_roots_moved_and_recreated_defers(self):
        self.link_target_outside_roots_replaced('move')

    def test_a_link_target_outside_the_roots_deleted_and_recreated_defers(self):
        self.link_target_outside_roots_replaced('delete')

    def test_a_link_moved_out_and_replaced_by_a_file_before_inspection_defers(self):
        target = self.directory('q1/target')
        def act():
            (self.cache/'n').symlink_to(target/'output')
            (self.cache/'n').rename(self.root/'outside-n')
            (self.cache/'n').write_text('regular file under the same name')
        self.assert_target_kept(target, act)

    def moved_out_and_replaced(self, replace, refreshes=1):
        target = self.directory('q1/target')
        work = self.cache/'work'
        (work/'x').mkdir(parents=True)
        (work/'y').mkdir(parents=True)
        def act():
            (work/'x').rename(self.root/'outside-x')
            linked = replace(work)
            for _ in range(refreshes):
                self.j.refresh_links()
            (linked/'link').symlink_to(target/'output')
        self.assert_target_kept(target, act)

    def test_a_directory_recreated_before_the_poll_stays_watched(self):
        def replace(work):
            (work/'x').mkdir()
            return work/'x'
        self.moved_out_and_replaced(replace)

    def test_a_subdirectory_of_a_recreated_directory_stays_watched(self):
        def replace(work):
            (work/'x'/'sub').mkdir(parents=True)
            return work/'x'/'sub'
        self.moved_out_and_replaced(replace)

    def test_a_sibling_renamed_into_place_stays_watched(self):
        def replace(work):
            (work/'y').rename(work/'x')
            return work/'x'
        self.moved_out_and_replaced(replace)

    def test_a_directory_moved_back_before_the_poll_stays_watched(self):
        def replace(work):
            (self.root/'outside-x').rename(work/'x')
            return work/'x'
        self.moved_out_and_replaced(replace)

    def test_a_recreated_directory_stays_watched_across_refreshes(self):
        def replace(work):
            (work/'x').mkdir()
            return work/'x'
        self.moved_out_and_replaced(replace, refreshes=2)

    def test_a_directory_replaced_during_discovery_stays_watched(self):
        target = self.directory('q1/target')
        work = self.cache/'work'
        (work/'x').mkdir(parents=True)
        original = janitor.ReferenceWatch.poll
        state = {'done': False}
        def poll(watch):
            if not state['done'] and (work/'x') in watch.directories.values():
                state['done'] = True
                (work/'x').rename(self.root/'outside-x')
                (work/'x').mkdir()
            return original(watch)
        with self.guards(), patch.object(janitor.ReferenceWatch, 'poll', poll):
            try:
                self.j.discover()
            except janitor.ReferenceChanged:
                pass
            self.addCleanup(lambda: self.j.watch and self.j.watch.close())
        self.assertTrue(state['done'])
        (work/'x'/'link').symlink_to(target/'output')
        with self.guards():
            self.j.apply = True
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def test_a_tree_moved_to_a_dangling_link_destination_is_walked(self):
        for variant in ('new link', 'existing relative link'):
            with self.subTest(variant=variant):
                self.setUp()
                target = self.directory('q1/target')
                destination = self.root/'outside'/'D'
                destination.parent.mkdir(parents=True)
                (self.cache/'L').symlink_to(destination)  # dangling at discovery
                moving = self.cache/'work'/'D'
                moving.mkdir(parents=True)
                if variant == 'existing relative link':
                    # Dangling where it is; it names the candidate once moved under outside/.
                    (moving/'r').symlink_to(Path('..')/'..'/'volume'/'q1'/'target')
                def act():
                    moving.rename(destination)
                    self.j.refresh_links()
                    if variant == 'new link':
                        (destination/'link').symlink_to(target/'output')
                self.assert_target_kept(target, act)

    def test_a_dangling_link_created_after_discovery_protects_a_tree_moved_there(self):
        target = self.directory('q1/target')
        destination = self.root/'outside'/'D'
        destination.parent.mkdir(parents=True)
        moving = self.cache/'work'/'D'
        moving.mkdir(parents=True)
        def act():
            (self.cache/'L').symlink_to(destination)  # read by poll(), dangling
            self.j.refresh_links()
            moving.rename(destination)
            self.j.refresh_links()
            (destination/'link').symlink_to(target/'output')
        self.assert_target_kept(target, act)

    def test_a_file_appearing_at_a_dangling_destination_is_ignored(self):
        old = self.directory('q1/old')
        destination = self.root/'outside'/'log.txt'
        destination.parent.mkdir(parents=True)
        (self.cache/'latest.log').symlink_to(destination)
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            destination.write_text('written after discovery')
            self.j.apply = True
            self.assertTrue(self.j.remove(old, kind='task'))

    def test_a_link_chain_appearing_at_a_dangling_destination_keeps_its_target(self):
        target = self.directory('q1/target')
        destination = self.root/'outside'/'D'
        destination.parent.mkdir(parents=True)
        (self.cache/'L').symlink_to(destination)
        def act():
            destination.symlink_to(target)
        self.assert_target_kept(target, act)

    def test_a_tree_moved_under_a_recreated_removed_candidate_is_walked(self):
        target = self.directory('q1/target')
        old = self.directory('q1/old')
        incoming = self.root/'incoming'
        incoming.mkdir()
        (incoming/'link').symlink_to(target/'output')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.j.apply = True
            self.assertTrue(self.j.remove(old, kind='task'))
            old.mkdir()
            self.j.refresh_links()
            (self.cache/'L').symlink_to(old/'sub')
            self.j.refresh_links()
            incoming.rename(old/'sub')
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def test_a_tree_arriving_while_its_link_is_read_is_walked(self):
        target = self.directory('q1/target')
        destination = self.root/'outside'/'D'
        destination.parent.mkdir(parents=True)
        incoming = self.root/'incoming'
        incoming.mkdir()
        (incoming/'link').symlink_to(target/'output')
        (self.cache/'L').symlink_to(destination)
        original = janitor.link_target
        def link_target(path):
            result = original(path)
            if str(path).endswith('/L') and not destination.exists():
                incoming.rename(destination)  # arrives after the destination was read missing
            return result
        with self.guards(), patch.object(janitor, 'link_target', link_target):
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.j.apply = True
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def racing_link_target(self, during):
        """link_target that runs during() for the link cache/L after reading its destination."""
        def link_target(path):
            try:
                path = Path(path)
                raw = Path(os.readlink(path))
                target = raw if raw.is_absolute() else path.parent/raw
                result = Path(os.path.abspath(target)), target.resolve(), target.is_dir()
            except OSError as error:
                return error
            if path.name == 'L':
                during()
            return result
        return link_target

    def test_a_directory_appearing_right_after_its_link_was_read_is_walked(self):
        target = self.directory('q1/target')
        destination = self.root/'outside'/'D'
        destination.parent.mkdir(parents=True)
        incoming = self.root/'incoming'
        incoming.mkdir()
        (incoming/'link').symlink_to(target/'output')
        (self.cache/'L').symlink_to(destination)
        def during():
            if not destination.exists():
                incoming.rename(destination)
        with self.guards(), patch.object(janitor, 'link_target', self.racing_link_target(during)):
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.j.apply = True
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def test_a_link_target_gone_before_its_walk_is_tracked(self):
        target = self.directory('q1/target')
        destination = self.root/'outside'/'D'
        destination.mkdir(parents=True)
        incoming = self.root/'incoming'
        incoming.mkdir()
        (incoming/'link').symlink_to(target/'output')
        (self.cache/'L').symlink_to(destination)
        def during():
            if destination.exists():
                destination.rmdir()  # read as a directory, gone before the walk lists it
        with self.guards(), patch.object(janitor, 'link_target', self.racing_link_target(during)):
            self.j.discover()
            self.addCleanup(self.j.watch.close)
        with self.guards():
            incoming.rename(destination)
            self.j.apply = True
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def test_a_listed_tree_moved_to_a_briefly_present_destination_is_walked(self):
        target = self.directory('q1/target')
        destination = self.root/'outside'/'D'
        destination.parent.mkdir(parents=True)
        tree = self.cache/'T'
        tree.mkdir()
        (self.cache/'L').symlink_to(destination)
        def during():
            if not destination.exists():
                destination.mkdir()
        with self.guards(), patch.object(janitor, 'link_target', self.racing_link_target(during)):
            self.j.discover()
            self.addCleanup(self.j.watch.close)
        with self.guards():
            destination.rmdir()
            tree.rename(destination)
            self.j.refresh_links()
            (destination/'link').symlink_to(target/'output')
            self.j.apply = True
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def test_a_file_destination_outside_the_view_replaced_by_a_moved_tree_is_walked(self):
        target = self.directory('q1/target')
        destination = self.root/'outside'/'X'
        destination.parent.mkdir(parents=True)
        destination.write_text('file')
        tree = self.cache/'T'
        tree.mkdir()
        (self.cache/'L').symlink_to(destination)
        def act():
            destination.unlink()
            tree.rename(destination)
            self.j.refresh_links()
            (destination/'link').symlink_to(target/'output')
        self.assert_target_kept(target, act)

    def test_a_new_links_directory_gone_before_its_walk_is_observed(self):
        target = self.directory('q1/target')
        destination = self.root/'outside'/'D'
        destination.mkdir(parents=True)
        tree = self.cache/'T'
        tree.mkdir()
        original = janitor.ReferenceWatch.poll
        state = {'armed': False}
        def poll(watch):
            result = original(watch)
            if state['armed'] and destination.exists():
                destination.rmdir()  # the walk of the poll's result finds it gone
                state['armed'] = False
            return result
        def act():
            (self.cache/'L').symlink_to(destination)
            state['armed'] = True
            with patch.object(janitor.ReferenceWatch, 'poll', poll):
                self.j.refresh_links()
            tree.rename(destination)
            self.j.refresh_links()
            (destination/'link').symlink_to(target/'output')
        self.assert_target_kept(target, act)

    def test_sentinels_are_few_and_follow_their_anchor(self):
        # A missing destination two levels below an unwatched directory: the sentinel moves
        # down as the levels appear, and a directory at the destination is walked.
        target = self.directory('q1/target')
        outside = self.root/'outside'
        outside.mkdir()
        (self.cache/'L').symlink_to(outside/'a'/'b'/'D')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.assertEqual(list(self.j.watch.sentinel_dirs.values()), [str(outside)])
            (outside/'a').mkdir()
            self.j.refresh_links()
            self.assertIn(str(outside/'a'), self.j.watch.sentinel_dirs.values())
            (outside/'a'/'b'/'D').mkdir(parents=True)
            self.j.refresh_links()
            (outside/'a'/'b'/'D'/'link').symlink_to(target/'output')
            self.j.apply = True
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def test_a_directory_moved_out_of_a_candidate_during_its_removal_stays_watched(self):
        first = self.directory('q1/first')
        (first/'sub').mkdir()
        second = self.directory('q1/second')
        active = self.cache/'active'
        active.mkdir()
        original = shutil.rmtree
        def rmtree(path, *args, **kwargs):
            if Path(path) == first:
                (first/'sub').rename(active/'sub')  # leaves during the removal
            return original(path, *args, **kwargs)
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            self.j.apply = True
            with patch.object(janitor.shutil, 'rmtree', rmtree):
                self.assertTrue(self.j.remove(first, kind='task'))
            self.j.refresh_links()
            (active/'sub'/'link').symlink_to(second/'output')
            try:
                self.assertFalse(self.j.remove(second, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(second.exists())

    def test_a_link_on_the_way_to_a_destination_is_followed_not_fatal(self):
        target = self.directory('q1/target')
        outside = self.root/'outside'
        (outside/'real'/'b').mkdir(parents=True)
        (self.cache/'L').symlink_to(outside/'a'/'b')
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            (outside/'a').symlink_to(outside/'real')
            self.j.refresh_links()  # no error: the link is looked at, its directory walked
            (outside/'real'/'b'/'link').symlink_to(target/'output')
            self.j.apply = True
            try:
                self.assertFalse(self.j.remove(target, kind='task'))
            except janitor.ReferenceChanged:
                pass
        self.assertTrue(target.exists())

    def test_an_unreadable_anchor_keeps_moves_out_of_the_view_unsafe(self):
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        locked = self.root/'locked'
        locked.mkdir()
        (self.cache/'L').symlink_to(locked/'missing')
        locked.chmod(0o311)  # searchable but not readable: no watch can be placed
        self.addCleanup(locked.chmod, 0o755)
        source = self.cache/'source'
        source.mkdir()
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            source.rename(self.root/'outside-source')
            self.j.apply = True
            with self.assertRaises(janitor.ReferenceChanged):
                self.j.remove(run, kind='run', hours=72)
        self.assertTrue(run.is_dir())

    def paused_walk(self, batches):
        """reference_paths patched to run out of time after `batches` batches."""
        calls = {'batches': 0}
        original = janitor.DirectoryIndex.stat_many
        def stat_many(index, paths):
            calls['batches'] += 1
            return original(index, paths)
        clock = {'now': 0.0}
        def monotonic():
            return 1e9 if calls['batches'] >= batches else clock['now']
        return calls, stat_many, monotonic

    def test_a_walk_out_of_time_is_continued_by_the_next_call(self):
        target = self.directory('q1/target')
        for index in range(6):
            (self.cache/f'd{index}'/'deeper').mkdir(parents=True)
        (self.cache/'d5'/'deeper'/'link').symlink_to(target/'output')
        watcher = janitor.ReferenceWatch()
        self.addCleanup(watcher.close)
        walk = janitor.Walk()
        calls, stat_many, monotonic = self.paused_walk(3)
        with patch.object(janitor.DirectoryIndex, 'batch_size', 1), \
             patch.object(janitor.DirectoryIndex, 'stat_many', stat_many), \
             patch('time.monotonic', monotonic):
            with self.assertRaises(janitor.WalkPaused):
                janitor.reference_paths([self.cache], seconds=1, watcher=watcher, index_root=self.cache, walk=walk)
        self.assertTrue(walk.pending)
        self.assertEqual(len(walk.visited), 3)
        walked = [path for path in walk.visited.values() if path != str(self.cache)]
        # A link created during the pause in a directory already walked is seen through its watch.
        (Path(walked[0])/'late-link').symlink_to(target/'late')
        found = janitor.reference_paths([self.cache], seconds=None, watcher=watcher, index_root=self.cache, walk=walk)
        self.assertIsNone(walk.pending)
        self.assertIn(target/'output', found)
        self.assertIn(target/'late', found)
        # The listings of the part walked before the pause stay in the index.
        index = janitor.DirectoryIndex(self.cache, seconds=None)
        self.addCleanup(index.close)
        rows = {path for (path,) in index.db.execute('SELECT path FROM directories')}
        self.assertTrue(set(walk.visited.values()) <= rows, set(walk.visited.values()) - rows)

    def test_a_continued_walk_adds_roots_that_appeared_meanwhile(self):
        target = self.directory('q1/target')
        (self.cache/'a'/'b').mkdir(parents=True)
        extra = self.root/'new-worktree'
        extra.mkdir()
        (extra/'link').symlink_to(target/'output')
        watcher = janitor.ReferenceWatch()
        self.addCleanup(watcher.close)
        walk = janitor.Walk()
        calls, stat_many, monotonic = self.paused_walk(1)
        with patch.object(janitor.DirectoryIndex, 'batch_size', 1), \
             patch.object(janitor.DirectoryIndex, 'stat_many', stat_many), \
             patch('time.monotonic', monotonic):
            with self.assertRaises(janitor.WalkPaused):
                janitor.reference_paths([self.cache], seconds=1, watcher=watcher, index_root=self.cache, walk=walk)
        found = janitor.reference_paths([self.cache, extra], seconds=None, watcher=watcher,
                                        index_root=self.cache, walk=walk)
        self.assertIn(target/'output', found)

    def test_a_walk_interrupted_by_another_error_starts_again(self):
        # Only a pause saves where a walk stands. A timeout mid-batch (a database wait) leaves a
        # half-filled walk that the next call must not take as finished.
        target = self.directory('q1/target')
        for index in range(6):
            (self.cache/f'd{index}').mkdir()
            (self.cache/f'd{index}'/'link').symlink_to(target/f'out{index}')
        watcher = janitor.ReferenceWatch()
        self.addCleanup(watcher.close)
        walk = janitor.Walk()
        calls = {'batches': 0}
        original = janitor.DirectoryIndex.stat_many
        def stat_many(index, paths):
            calls['batches'] += 1
            if calls['batches'] == 3:
                raise TimeoutError('database wait')
            return original(index, paths)
        with patch.object(janitor.DirectoryIndex, 'batch_size', 1), \
             patch.object(janitor.DirectoryIndex, 'stat_many', stat_many):
            with self.assertRaises(TimeoutError) as raised:
                janitor.reference_paths([self.cache], seconds=None, watcher=watcher, index_root=self.cache, walk=walk)
        self.assertNotIsInstance(raised.exception, janitor.WalkPaused)
        self.assertIsNone(walk.pending)
        found = janitor.reference_paths([self.cache], seconds=None, watcher=watcher, index_root=self.cache, walk=walk)
        expected = {target/f'out{index}' for index in range(6)}
        self.assertTrue(expected <= set(found), expected-set(found))

    def test_discovery_attempts_continue_one_walk(self):
        target = self.directory('q1/target')
        (self.cache/'deep'/'er').mkdir(parents=True)
        (self.cache/'deep'/'er'/'link').symlink_to(target/'output')
        calls = []
        original = janitor.reference_paths
        def reference_paths(roots, **options):
            calls.append(options.get('walk'))
            if len(calls) == 1:
                options['walk'].pending = [str(root) for root in roots]  # stopped before the first batch
                raise TimeoutError('run-cache index exceeded 1s; the walk continues in the next attempt')
            return original(roots, **options)
        with self.guards(), patch.object(janitor, 'reference_paths', reference_paths):
            self.assertTrue(self.j.ensure_discovery())
            self.addCleanup(self.j.watch.close)
        self.assertEqual(len(calls), 2)
        self.assertIs(calls[0], calls[1])
        self.assertIsNone(self.j.walk)
        self.assertIn(target/'output', self.j.links)

    def test_too_many_unverified_names_fail_closed(self):
        watcher = janitor.ReferenceWatch()
        self.addCleanup(watcher.close)
        watcher.add(self.cache)
        with patch.object(janitor, 'MAX_UNVERIFIED_NAMES', 3):
            for index in range(5):
                (self.cache/f'short-{index}').write_text('x')
                (self.cache/f'short-{index}').unlink()
            with self.assertRaisesRegex(OSError, 'too many short-lived names'):
                watcher.poll()
        self.assertTrue(watcher.broken)

    def test_events_beyond_the_kernel_queue_are_kept_between_polls(self):
        # More changes than fs.inotify.max_queued_events arrive while nothing polls (index
        # pruning, process scans and removals do not poll); none may be lost to an overflow.
        limit = int(Path('/proc/sys/fs/inotify/max_queued_events').read_text())
        busy = self.cache/'busy'
        busy.mkdir()
        watcher = janitor.ReferenceWatch()
        self.addCleanup(watcher.close)
        watcher.add(busy)
        for index in range(limit+2000):
            (busy/f'{index}').mkdir()
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        (busy/f'{limit+2000}').symlink_to(run/'output')
        pending = watcher.poll()
        self.assertFalse(watcher.broken)
        self.assertEqual(len(pending), limit+2000)
        self.assertIn(run/'output', watcher.references)

    def test_unpolled_events_beyond_the_memory_cap_fail_closed(self):
        busy = self.cache/'busy'
        busy.mkdir()
        with patch.object(janitor, 'MAX_BUFFERED_EVENT_BYTES', 4096):
            watcher = janitor.ReferenceWatch()
            self.addCleanup(watcher.close)
            watcher.add(busy)
            for index in range(2000):
                (busy/f'{index}').mkdir()
            deadline = time.monotonic()+10
            while not watcher.broken and time.monotonic() < deadline:
                (busy/f'extra-{time.monotonic_ns()}').mkdir()
                time.sleep(.05)
            self.assertTrue(watcher.broken)
            with self.assertRaisesRegex(OSError, 'invalid'):
                watcher.poll()

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

    def test_promoted_suite_empty_json_and_json_named_directories(self):
        work = self.directory('q1/sheriff-S20-example')
        logged = self.directory('q1/logged-output')
        declared = self.directory('q1/nested-output')
        unpromoted = self.directory('q1/sheriff-S21-unpromoted')
        logs = work/'project/Library/Unbound/ProjectView/Logs'
        logs.mkdir(parents=True)
        (logs/'Output.log.json').write_text('')
        (logs/'Blank.log.json').write_text(' \n\n')
        (logs/'Lines.log.json').write_text(json.dumps({'Message': 'a\u2028b'}, ensure_ascii=False)+'\n'+json.dumps({'artifact': str(logged)})+'\n')
        nested = work/'probe/report.json'
        nested.mkdir(parents=True)
        (nested/'metadata.json').write_text(json.dumps({'artifact': str(declared)}))
        self.promote_fixture(work)
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            candidates = {path for path, _, _ in self.j.candidates()}
            self.assertNotIn(work, candidates)
            self.assertNotIn(declared, candidates)
            self.assertNotIn(logged, candidates)
            self.j.apply = True
            self.assertFalse(self.j.remove(declared, kind='task'))
            self.assertFalse(self.j.remove(logged, kind='task'))
            self.assertIn(unpromoted, candidates)

    def unreadable_suite_fails_closed(self, write, expected):
        work = self.directory('q1/sheriff-S20-example')
        declared = self.directory('q1/json-output')
        write(work, declared)
        self.promote_fixture(work)
        with patch.object(janitor.time, 'time', return_value=self.future), self.guards():
            code = janitor.main(['--repo', str(self.repo), '--apply'])
        self.assertEqual(code, 1)
        logs = list((self.cache/'agents/janitor').glob('*.jsonl'))
        rows = [json.loads(line) for line in logs[0].read_text().splitlines()]
        self.assertIn(expected, next(row for row in rows if row['action'] == 'error')['error'])
        self.assertTrue(rows[-1]['failed'])
        self.assertEqual(rows[-1]['removals'], 0)
        self.assertTrue(declared.exists())

    def test_promoted_suite_truncated_json_stops_discovery(self):
        def write(work, declared):
            (work/'report.json').write_text('{"artifact": "'+str(declared)+'"')
        self.unreadable_suite_fails_closed(write, 'Expecting')

    def test_promoted_suite_truncated_json_lines_stop_discovery(self):
        def write(work, declared):
            (work/'Output.log.json').write_text(json.dumps({'Message': 'a'})+'\n{"artifact": "'+str(declared)+'"\n')
        self.unreadable_suite_fails_closed(write, 'Extra data')

    @unittest.skipIf(os.geteuid() == 0, 'root can search any directory')
    def test_promoted_suite_unreadable_json_stops_discovery(self):
        def write(work, declared):
            sealed = work/'sealed'
            sealed.mkdir()
            (sealed/'metadata.json').write_text(json.dumps({'artifact': str(declared)}))
            sealed.chmod(0o444)
            self.addCleanup(sealed.chmod, 0o755)
        self.unreadable_suite_fails_closed(write, 'Permission denied')

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

    def test_replacement_name_ambiguity_stops_reconciliation(self):
        target = self.directory('q1/target')
        source = self.directory('moving')
        destination = self.volume/'moved'
        with self.guards():
            self.j.discover()
            self.addCleanup(self.j.watch.close)
            source.rename(destination)
            source.mkdir()
            # A retry can register the replacement before draining the rename.
            self.j.watch.add(source)
            self.j.apply = True
            with self.assertRaises(janitor.ReferenceChanged):
                self.j.refresh_links()
            (destination/'new-link').symlink_to(target/'output')
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
