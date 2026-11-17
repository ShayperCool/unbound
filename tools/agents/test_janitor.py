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
import run_cache_index


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
    def guards(self, processes=()):
        with patch.object(janitor, 'worktrees', return_value=[(self.repo, '', 'main')]), \
             patch.object(janitor, 'main_checkout', return_value=self.repo), \
             patch.object(janitor, 'process_paths', return_value=set(processes)), \
             patch.object(janitor.runtime_snapshot, 'cleanup'), \
             patch.object(janitor.Janitor, 'kache_gc'), \
             patch.object(janitor.time, 'time', return_value=self.future):
            yield

    def instances(self, probe, names):
        """Instances of runs/<probe>/ in age order (the last is the newest)."""
        self.aged = [self.directory(f'unbound-validation/runs/{probe}/{name}') for name in names]
        return self.aged

    def collect_applied(self, processes=()):
        # Instance ages are set last: adding content inside an instance changes its time.
        for age, path in enumerate(getattr(self, 'aged', [])):
            os.utime(path, (1_000_000+age, 1_000_000+age))
        with self.guards(processes):
            self.j.apply = True
            self.j.collect()

    def rows(self, action=None):
        rows = [json.loads(line) for line in self.output.getvalue().splitlines() if line.startswith('{')]
        return [row for row in rows if action is None or row['action'] == action]

    # Validation runs

    def test_symlinked_run_is_kept_and_unlinked_old_run_is_planned(self):
        linked = self.directory('unbound-validation/runs/probe-20261101-old')
        old = self.directory('unbound-validation/runs/probe-20261102-old')
        (self.cache/'retained').symlink_to(linked/'output')
        with self.guards():
            self.j.collect()
        self.assertEqual(self.j.removed, 1)
        self.assertEqual([row['path'] for row in self.rows('would_remove')], [str(old)])
        self.assertTrue(linked.is_dir())
        self.assertTrue(old.is_dir())  # dry-run did not delete

    def test_old_instances_of_a_probe_are_removed_except_the_newest(self):
        old, older, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        self.collect_applied()
        self.assertFalse(old.exists())
        self.assertFalse(older.exists())
        self.assertTrue(newest.is_dir())

    def test_instances_in_a_probes_runs_folder_are_removed_except_the_newest(self):
        first, second, newest = self.instances('probe/runs', ['native1', 'native2', 'native3'])
        beside = self.instances('probe', ['gate-1', 'gate-2'])
        self.aged = [first, second, newest, *beside]
        self.collect_applied()
        self.assertFalse(first.exists())
        self.assertFalse(second.exists())
        self.assertTrue(newest.is_dir())
        self.assertFalse(beside[0].exists())
        self.assertTrue(beside[1].is_dir())  # each folder keeps its own newest run

    def test_sheriff_and_baseline_runs_stay(self):
        sheriff, baseline, plain, newest = self.instances(
            'probe', ['suite-sheriff-S9-abc', 'baseline-2', 'suite-land-q1-s2', 'gate-9'])
        dated = self.directory('unbound-validation/runs/sheriff-20261101')
        self.collect_applied()
        self.assertTrue(sheriff.is_dir())
        self.assertTrue(baseline.is_dir())
        self.assertTrue(dated.is_dir())
        self.assertFalse(plain.exists())
        self.assertTrue(newest.is_dir())

    def test_a_link_inside_a_removed_run_protects_nothing(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (first/'deep'/'er').mkdir(parents=True)
        (first/'deep'/'er'/'link').symlink_to(second/'output')
        (second/'alias').symlink_to(second/'output')  # a run's own link does not keep it
        self.collect_applied()
        self.assertFalse(first.exists())
        self.assertFalse(second.exists())
        self.assertTrue(newest.is_dir())

    def test_a_run_protected_from_outside_keeps_what_its_own_links_name(self):
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        (self.cache/'retained').symlink_to(second/'output')
        (second/'link').symlink_to(first/'output')
        self.collect_applied()
        self.assertTrue(second.is_dir())
        self.assertTrue(first.is_dir())  # named from inside a kept run
        self.assertFalse(third.exists())

    def test_a_run_in_live_use_keeps_what_its_own_links_name(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (second/'link').symlink_to(first/'output')
        self.collect_applied(processes=[second/'output'])
        self.assertTrue(second.is_dir())
        self.assertTrue(first.is_dir())

    def test_a_recently_written_run_keeps_what_its_own_links_name(self):
        # Old by its own timestamps but written inside lately.
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        (first/'link').symlink_to(third/'output')
        os.utime(first/'output', (self.future, self.future))
        self.collect_applied()
        self.assertTrue(first.is_dir())
        self.assertTrue(third.is_dir())
        self.assertFalse(second.exists())

    def test_a_recently_written_run_protects_older_runs_it_names(self):
        # The run that names another is newer, so a removal in age order would reach the named
        # run first.
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        (third/'link').symlink_to(first/'output')
        os.utime(third/'output', (self.future, self.future))
        self.collect_applied()
        self.assertTrue(third.is_dir())
        self.assertTrue(first.is_dir())
        self.assertFalse(second.exists())
        self.assertEqual(self.rows('error'), [])

    def test_a_run_named_from_another_old_run_goes_after_it(self):
        # The newer run names the older one: it is removed first, and then nothing names the older.
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (second/'link').symlink_to(first/'output')
        self.collect_applied()
        self.assertFalse(first.exists())
        self.assertFalse(second.exists())
        self.assertEqual([row['path'] for row in self.rows('removed')], [str(second), str(first)])

    def test_dry_run_counts_links_inside_runs_it_would_remove_as_gone(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        (second/'link').symlink_to(first/'output')
        for age, path in enumerate(self.aged):
            os.utime(path, (1_000_000+age, 1_000_000+age))
        with self.guards():
            self.j.collect()
        self.assertEqual({row['path'] for row in self.rows('would_remove')}, {str(first), str(second)})
        self.assertTrue(first.is_dir())

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

    def test_runs_and_labels_named_by_agent_records_are_kept(self):
        first, second, third, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3', 'gate-4'])
        labels = [self.directory(f'unbound-validation/suite/{name}') for name in ('land-a', 'land-b', 'land-c')]
        (self.cache/'validation').symlink_to(self.volume/'unbound-validation')
        records = self.cache/'agents'
        for name in ('baseline-captures', 'postland'):
            (records/name).mkdir()
        (records/'baseline-captures/0a.json').write_text(json.dumps({'run': f'{self.cache}/validation/runs/probe/gate-1/x'}))
        (records/'postland/round.jsonl').write_text(json.dumps({'work': f'{self.cache}/validation/suite/land-a'})+'\n')
        (records/'postland/20261116-probe.json').write_text(json.dumps({'out': str(second/'output')}))
        self.collect_applied()
        self.assertTrue(first.is_dir())
        self.assertTrue(second.is_dir())
        self.assertFalse(third.exists())
        self.assertTrue(labels[0].is_dir())
        self.assertFalse(labels[1].exists())
        self.assertFalse(labels[2].exists())

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

    def test_no_run_or_suite_label_is_removed_until_every_reference_file_is_read(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        label = self.directory('unbound-validation/suite/land-a')
        task = self.directory('q1/old')
        suite = self.volume/'unbound-validation/suite/sheriff-S9-abc'
        suite.mkdir(parents=True)
        (suite/'summary.json').write_text('{}')
        with patch.object(janitor, 'SUITE_REFERENCE_SECONDS', 0):
            self.collect_applied()
        self.assertTrue(first.is_dir())
        self.assertTrue(label.is_dir())
        self.assertFalse(task.exists())  # other kinds do not depend on those references
        self.assertIn('references not all read yet', self.output.getvalue())
        self.assertTrue(self.j.partial)

    # Suite labels

    def test_old_unreferenced_suite_labels_go_and_others_stay(self):
        old = self.directory('unbound-validation/suite/land-1-old')
        recent = self.directory('unbound-validation/suite/land-2-recent')
        live = self.directory('unbound-validation/suite/land-3-live')
        linked = self.directory('unbound-validation/suite/land-4-linked')
        sheriff = self.directory('unbound-validation/suite/sheriff-S9-abc')
        promoted = self.directory('unbound-validation/suite/sheriff-S8-old-promoted-copy')
        days = janitor.DEFAULT_SUITE_DAYS*86400
        os.utime(recent/'output', (self.future-days+3600, self.future-days+3600))
        (self.cache/'kept').symlink_to(linked/'output')
        self.collect_applied(processes=[live/'output'])
        self.assertFalse(old.exists())
        for path in (recent, live, linked, sheriff, promoted):
            self.assertTrue(path.is_dir(), path)

    def test_suite_age_is_configurable(self):
        label = self.directory('unbound-validation/suite/land-1')
        self.j.suite_days = 11  # older than the 10 days of the fixture clock
        self.collect_applied()
        self.assertTrue(label.is_dir())

    # Writes during the walk and the removals

    def test_a_live_editor_rotating_folders_in_a_run_does_not_stop_cleanup(self):
        first, second, live = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        rotating = live/'project/Library/ShaderCache/vfx/Effect'
        (rotating/'02').mkdir(parents=True)
        (rotating/'02/link').symlink_to(second/'output')
        stop = threading.Event()
        def rotate():
            # A folder renamed back and forth as fast as possible, as an Editor rotates its cache.
            while not stop.is_set():
                for source, destination in (('02', '03'), ('03', '02')):
                    (rotating/source).rename(rotating/destination)
                    time.sleep(0.005)
        thread = threading.Thread(target=rotate)
        thread.start()
        try:
            with patch.object(janitor, 'process_paths', return_value=set()):
                self.collect_applied(processes=[live/'output'])
        finally:
            stop.set()
            thread.join(5)
        self.assertFalse(self.j.partial)
        self.assertFalse(first.exists())
        self.assertTrue(second.is_dir())  # named from the live run under either name
        self.assertTrue(live.is_dir())

    def test_a_folder_renamed_in_place_during_the_walk_is_walked_under_its_new_name(self):
        old, newest = self.instances('probe', ['gate-1', 'gate-2'])
        holder = self.directory('holder')
        (holder/'02').mkdir()
        (holder/'02/link').symlink_to(old/'output')
        original = run_cache_index.DirectoryIndex.stat_many
        renamed = []
        def rename_first(index, paths):
            if str(holder/'02') in map(str, paths) and not renamed:
                (holder/'02').rename(holder/'03')
                renamed.append(True)
            return original(index, paths)
        with patch.object(run_cache_index.DirectoryIndex, 'stat_many', rename_first):
            self.collect_applied()
        self.assertTrue(renamed)
        self.assertTrue(old.is_dir())

    def test_a_link_in_a_directory_changing_while_listed_still_protects(self):
        old, newest = self.instances('probe', ['gate-1', 'gate-2'])
        holder = self.directory('holder')
        (holder/'link').symlink_to(old/'output')
        def changing(index, path, stamp):
            raise run_cache_index.DirectoryChanged(f'run-cache directory changed during indexing: {path}')
        with patch.object(run_cache_index.DirectoryIndex, 'read', changing):
            self.collect_applied()
        self.assertTrue(old.is_dir())
        self.assertFalse(self.j.partial)
        self.assertEqual(self.rows('links')[0]['unsettled'], 0)  # each changed place was listed again

    def test_places_still_changing_after_the_last_recheck_are_logged(self):
        holder = self.directory('holder')
        def changing(index, path, stamp):
            raise run_cache_index.DirectoryChanged(f'run-cache directory changed during indexing: {path}')
        with patch.object(run_cache_index.DirectoryIndex, 'read', changing), \
             patch.object(janitor, 'RECHECK_ROUNDS', 0):
            self.collect_applied()
        links = self.rows('links')[0]
        self.assertGreater(links['unsettled'], 0)
        self.assertEqual(len(links['unsettled_examples']), min(5, links['unsettled']))
        self.assertTrue(holder.is_dir())

    def test_a_directory_vanishing_during_the_walk_is_skipped(self):
        old, newest = self.instances('probe', ['gate-1', 'gate-2'])
        vanishing = self.directory('vanishing')
        original = run_cache_index.DirectoryIndex.entries
        def gone(index, path, info):
            if path == str(vanishing):
                raise FileNotFoundError(2, 'No such file or directory', path)
            return original(index, path, info)
        with patch.object(run_cache_index.DirectoryIndex, 'entries', gone):
            self.collect_applied()
        self.assertFalse(old.exists())
        self.assertFalse(self.j.partial)

    @unittest.skipIf(os.geteuid() == 0, 'root can list any directory')
    def test_a_vanished_link_target_outside_the_roots_does_not_list_its_parent(self):
        # Its parent was never listed (an unreadable /tmp sibling must not fail the walk).
        old, newest = self.instances('probe', ['gate-1', 'gate-2'])
        outside = self.root/'outside'
        (outside/'target').mkdir(parents=True)
        (outside/'private').mkdir()
        (outside/'private').chmod(0)
        self.addCleanup((outside/'private').chmod, 0o755)
        (self.cache/'build').symlink_to(outside/'target')
        original = run_cache_index.DirectoryIndex.stat_many
        def vanishing(index, paths):
            infos = original(index, paths)
            return [FileNotFoundError(2, 'No such file or directory', path) if path == str(outside/'target') else info
                    for path, info in zip(paths, infos)]
        with patch.object(run_cache_index.DirectoryIndex, 'stat_many', vanishing):
            self.collect_applied()
        self.assertFalse(old.exists())
        self.assertEqual(self.rows('links')[0]['unsettled'], 0)

    def test_runs_in_a_sheriff_or_baseline_probe_folder_stay(self):
        first, newest = self.instances('baseline-probe', ['gate-1', 'gate-2'])
        self.collect_applied()
        self.assertTrue(first.is_dir())
        self.assertTrue(newest.is_dir())

    def test_a_link_walk_out_of_time_removes_nothing_and_still_runs_cache_gc(self):
        old, newest = self.instances('probe', ['gate-1', 'gate-2'])
        with self.guards(), patch.object(janitor, 'link_walk', side_effect=TimeoutError('link walk exceeded 1s')), \
             patch.object(janitor.Janitor, 'kache_gc') as gc:
            self.j.apply = True
            self.j.collect()
            gc.assert_called_once()
        self.assertTrue(old.is_dir())
        self.assertTrue(self.j.partial)
        self.assertIn('link walk exceeded', self.rows('stopped')[0]['reason'])

    def test_unreadable_reference_scan_aborts_before_removal(self):
        self.directory('q1')
        with self.guards(), patch.object(janitor, 'link_walk', side_effect=OSError('unreadable')):
            with self.assertRaises(OSError):
                self.j.collect()
        self.assertEqual(self.j.removed, 0)

    def test_a_link_created_after_the_walk_protects_once_the_walk_is_repeated(self):
        first, second, newest = self.instances('probe', ['gate-1', 'gate-2', 'gate-3'])
        for age, path in enumerate(self.aged):
            os.utime(path, (1_000_000+age, 1_000_000+age))
        with self.guards():
            self.j.apply = True
            self.j.discover()
            (self.cache/'late-reference').symlink_to(second/'output')
            with patch.object(janitor, 'LINK_SNAPSHOT_SECONDS', 0):
                self.assertFalse(self.j.remove(second, 'run', 24))
            self.assertTrue(self.j.remove(first, 'run', 24))
        self.assertTrue(second.is_dir())

    def test_processes_are_read_again_after_the_guard_interval(self):
        paths = [self.directory(f'q1/old-{n}') for n in range(3)]
        with self.guards(), patch.object(janitor, 'process_paths',
                                         side_effect=[set(), set(), {paths[2]/'output'}]) as processes:
            self.j.apply = True
            self.j.discover()
            self.assertTrue(self.j.remove(paths[0], 'task', 24))  # within the interval: one reading
            self.assertEqual(processes.call_count, 1)
            with patch.object(janitor, 'PROCESS_GUARD_SECONDS', 0):
                self.assertTrue(self.j.remove(paths[1], 'task', 24))
                self.assertFalse(self.j.remove(paths[2], 'task', 24))
        self.assertTrue(paths[2].exists())

    def test_queue_and_promotion_are_rechecked_between_removals(self):
        paths = [self.directory(f'q1/old-{n}') for n in range(3)]
        work = self.directory('q1/sheriff-S20-example')
        with self.guards():
            self.j.discover()
            self.j.apply = True
            self.assertTrue(self.j.remove(paths[0], 'task', 24))
            self.promote_fixture(work, artifact=str(paths[1]))
            self.assertFalse(self.j.remove(paths[1], 'task', 24))
            (self.cache/'agents/QUEUE.md').write_text('| Q1 | infra | now | active | worker |\n')
            self.assertFalse(self.j.remove(paths[2], 'task', 24))
        self.assertTrue(paths[1].exists())
        self.assertTrue(paths[2].exists())

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

    # Tasks, targets and runtimes

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
        for n in range(1, 11):
            target = janitor.build.default_target(self.volume/f'slots/slot-{n:02}', self.builds)
            target.mkdir()
            self.assertEqual(self.j.check(target, 24)[0], 'protected checkout, slot or train')
        train = self.directory('land-trains/integration')
        self.assertEqual(self.j.check(train, 24)[0], 'protected checkout, slot or train')
        main_target = janitor.build.default_target(self.repo, self.builds)
        main_target.mkdir(exist_ok=True)
        self.assertEqual(self.j.check(main_target, 24)[0], 'protected checkout, slot or train')

    def test_recent_descendant_keeps_old_parent(self):
        path = self.directory('unbound-validation/runs/probe-20261101')
        os.utime(path/'output', (self.future, self.future))
        with patch.object(janitor.time, 'time', return_value=self.future):
            self.assertEqual(self.j.check(path, 72)[0], 'recently modified')

    def test_process_reference_and_symlink_chain_are_kept(self):
        path = self.directory('unbound-validation/runs/probe-20261101')
        alias = self.cache/'alias'
        alias.symlink_to(path, target_is_directory=True)
        (self.cache/'chain').symlink_to(alias/'output')
        self.j.links, _ = janitor.link_walk([self.cache])
        self.assertTrue(self.j.check(path, 72)[0].startswith('symlink reference'))
        self.j.links = []
        self.j.processes = janitor.PathSet([path/'output'])
        self.assertEqual(self.j.check(path, 72)[0], 'live process')

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
            self.assertTrue(self.j.remove(path, 'task', 24))
        self.assertFalse(path.exists())
        self.assertEqual(live.stat().st_mode & 0o777, 0o444)
        self.assertEqual(self.j.reclaimable, expected)  # directory only, linked file remains

    def test_active_task_rechecked_before_delete(self):
        path = self.directory('q1/old')
        (self.cache/'agents/QUEUE.md').write_text('| Q1 | infra | new | active | worker |\n')
        with self.guards():
            self.j.apply = True
            self.assertFalse(self.j.remove(path, 'task', 24))
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
        run = self.directory('unbound-validation/runs/probe-20261101-old')
        cleanup = janitor.runtime_snapshot.cleanup
        with patch.object(janitor.runtime_snapshot, 'active_snapshots', return_value=set()):
            removed = janitor.runtime_snapshot.cleanup(cache=cache, dry_run=True)
            self.assertEqual(removed, [snapshot.name])
            self.assertTrue(snapshot.is_dir())
            removed = janitor.runtime_snapshot.cleanup(cache=cache, remove=lambda _: False)
            self.assertEqual(removed, [])
            self.assertTrue(snapshot.is_dir())
            with self.guards():
                self.j.apply = True
                self.j.discover()
                remove = lambda path: self.j.remove(path, 'runtime', self.j.idle_hours)
                self.j.links = [(str(self.cache/'runtime'), str(snapshot/'unbound'))]
                self.assertEqual(cleanup(cache=cache, remove=remove), [])
                self.assertTrue(snapshot.is_dir())
                # A link inside a run removed in this run protects nothing.
                self.j.links = [(str(run/'runtime'), str(snapshot/'unbound'))]
                self.j.removed_paths.add(str(run))
                self.assertEqual(cleanup(cache=cache, remove=remove), [snapshot.name])
                self.assertFalse(snapshot.exists())

    def promote_fixture(self, work, **metadata):
        baseline = self.cache/'agents/baseline'
        baseline.mkdir(parents=True, exist_ok=True)
        (baseline/'summary.json').write_text(json.dumps(dict(
            label='sheriff-S20-example', work=str(work), **metadata)))

    def candidate_paths(self):
        return {path for path, _, _ in self.j.candidates.values()}

    def test_promoted_suite_and_all_declared_references_are_not_removed(self):
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
            self.j.apply = True
            for protected in (work, linked, declared, command, printed):
                self.assertFalse(self.j.remove(protected, 'task', 24))
            self.assertIn(unpromoted, self.candidate_paths())
            self.assertTrue(self.j.remove(unpromoted, 'task', 24))
        self.assertTrue((linked/'output').exists())

    def test_promoted_suite_empty_json_and_json_named_directories(self):
        work = self.directory('q1/sheriff-S20-example')
        logged = self.directory('q1/logged-output')
        declared = self.directory('q1/nested-output')
        logs = work/'project/Library/Unbound/ProjectView/Logs'
        logs.mkdir(parents=True)
        (logs/'Output.log.json').write_text('')
        (logs/'Blank.log.json').write_text(' \n\n')
        (logs/'Lines.log.json').write_text(json.dumps({'Message': 'a b'}, ensure_ascii=False)+'\n'+json.dumps({'artifact': str(logged)})+'\n')
        nested = work/'probe/report.json'
        nested.mkdir(parents=True)
        (nested/'metadata.json').write_text(json.dumps({'artifact': str(declared)}))
        self.promote_fixture(work)
        with self.guards():
            self.j.discover()
            self.j.apply = True
            self.assertFalse(self.j.remove(declared, 'task', 24))
            self.assertFalse(self.j.remove(logged, 'task', 24))

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

    def test_metadata_edit_of_the_promoted_suite_protects_from_the_next_walk(self):
        work = self.directory('q1/sheriff-S20-example')
        first = self.directory('q1/first')
        self.promote_fixture(work)
        with self.guards():
            self.j.discover()
            self.j.apply = True
            (work/'later.json').write_text(json.dumps({'artifact': str(first)}))
            with patch.object(janitor, 'LINK_SNAPSHOT_SECONDS', 0):
                self.assertFalse(self.j.remove(first, 'task', 24))
        self.assertTrue(first.exists())

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
            self.j.apply = True
            for path in (metadata, logged, direct):
                self.assertFalse(self.j.remove(path, 'task', 24))
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
            self.j.apply = True
            for path in (redirected, adjacent, quoted, absolute):
                self.assertFalse(self.j.remove(path, 'task', 24))

    def test_shortened_command_and_human_summary_do_not_protect_cache_ancestors(self):
        value = {'command': 'tool '+('x'*389)+' /home', 'summary': '3/10 values; /home'}
        self.assertEqual(len(value['command']), 400)
        self.assertEqual(list(janitor.suite_references(value)), [])

    def test_write_during_directory_permission_preparation_is_kept(self):
        path = self.directory('q1/old')
        path.chmod(0o555)
        original = janitor.os.fchmod
        def concurrent_write(descriptor, mode):
            original(descriptor, mode)
            (path/'output').write_text('new output')
        self.j.apply = True
        time.sleep(0.5)
        with patch.object(janitor, 'process_paths', return_value=set()), \
             patch.object(janitor, 'worktrees', return_value=[(self.repo, '', 'main')]), \
             patch.object(janitor, 'main_checkout', return_value=self.repo):
            self.j.discover()
            with patch.object(janitor.os, 'fchmod', side_effect=concurrent_write):
                self.assertFalse(self.j.remove(path, 'task', 0.0001))
        self.assertEqual((path/'output').read_text(), 'new output')

    def test_real_clock_old_directory_survives_permission_preparation(self):
        path = self.directory('q1/old')
        path.chmod(0o555)
        self.j.apply = True
        time.sleep(0.5)
        with patch.object(janitor, 'process_paths', return_value=set()), \
             patch.object(janitor, 'worktrees', return_value=[(self.repo, '', 'main')]), \
             patch.object(janitor, 'main_checkout', return_value=self.repo):
            self.assertTrue(self.j.remove(path, 'task', 0.0001))
        self.assertFalse(path.exists())

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

    def test_timer_hourly_and_preserves_cache_locations(self):
        units = install_janitor.units(self.repo, dict(os.environ))
        self.assertIn('OnCalendar=hourly', units['unbound-janitor.timer'])
        self.assertIn('Persistent=true', units['unbound-janitor.timer'])
        self.assertIn(str(self.builds), units['unbound-janitor.service'])
        self.assertIn('--apply --repo', units['unbound-janitor.service'])
        self.assertIn('WorkingDirectory='+str(self.repo)+'\n', units['unbound-janitor.service'])


if __name__ == '__main__':
    unittest.main()
