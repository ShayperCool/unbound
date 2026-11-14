#!/usr/bin/env python3
"""Content identity, concurrent publication, permissions, and collection of probe runtimes."""
import concurrent.futures
import contextlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import unittest
from unittest import mock

import runtime_snapshot as runtime


def capture(cli, managed, destination, cache):
    return str(runtime.snapshot_runtime(cli, managed, destination, cache=cache).resolve())


class RuntimeSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        # Fixture references belong to a task even in a detached integration
        # checkout. Tests of main and live ownership override this explicitly.
        owner = mock.patch.object(runtime, 'task_owner', return_value={
            'worktree': str(self.root), 'branch': 'fixture-task'})
        owner.start()
        self.addCleanup(owner.stop)
        self.cli = self.root/'build/release/unbound'
        self.cli.parent.mkdir(parents=True)
        self.cli.write_bytes(b'#!/bin/sh\nexit 0\n')
        self.cli.chmod(0o755)
        self.managed = self.root/'build/managed'
        self.managed.mkdir()
        (self.managed/'Unbound.Host.dll').write_bytes(b'host')
        (self.managed/'nested').mkdir()
        (self.managed/'nested/data').write_bytes(b'input')
        self.cache = self.root/'cache'

    def tearDown(self):
        self.temp.cleanup()

    def snapshot(self, name='run', **kwargs):
        return runtime.snapshot_runtime(self.cli, self.managed, self.root/name/'runtime',
                                        cache=self.cache, **kwargs)

    def test_reuses_all_contents_across_workers_and_keeps_build_outputs_independent(self):
        first, second = self.snapshot(), self.snapshot('other')
        self.assertTrue(first.is_symlink())
        self.assertEqual(first.resolve(), second.resolve())
        self.assertNotEqual(os.stat(first/'unbound').st_ino, self.cli.stat().st_ino)
        self.cli.write_bytes(b'changed executable')
        self.assertEqual((first/'unbound').read_bytes(), b'#!/bin/sh\nexit 0\n')
        self.assertNotEqual(self.snapshot('third').resolve(), first.resolve())
        (first.parent/'output.log').write_text('mutable output stays in run')

    def test_key_covers_every_file_name_content_and_executable_bit(self):
        previous = self.snapshot().resolve()
        path = self.managed/'nested/data'
        timestamp = path.stat().st_mtime_ns
        path.write_bytes(b'other')
        os.utime(path, ns=(timestamp, timestamp))
        changed = self.snapshot('changed').resolve()
        self.assertNotEqual(previous, changed)
        path.rename(path.with_name('renamed'))
        renamed = self.snapshot('renamed').resolve()
        self.assertNotEqual(changed, renamed)
        path = path.with_name('renamed')
        path.chmod(0o755)
        mode = self.snapshot('mode').resolve()
        self.assertNotEqual(renamed, mode)
        (self.managed/'new.dll').write_bytes(b'new')
        self.assertNotEqual(mode, self.snapshot('new').resolve())
        current = self.snapshot('before-version').resolve()
        with mock.patch.object(runtime, 'FORMAT_VERSION', 2):
            self.assertNotEqual(self.snapshot('version').resolve(), current)

    def test_manifest_checks_keep_all_inputs_and_reject_mismatch(self):
        first = self.snapshot()
        manifest = json.loads((first/runtime.MANIFEST).read_text())
        self.assertEqual(set(manifest), {'unbound', 'managed/Unbound.Host.dll', 'managed/nested/data'})
        self.assertEqual(first.resolve(), self.snapshot('matching', expected=manifest).resolve())
        manifest['../outside'] = 'bad'
        with self.assertRaisesRegex(ValueError, 'supplied manifest'):
            self.snapshot('bad', expected=manifest)

    def test_run_cannot_write_replace_or_add_snapshot_files(self):
        snapshot = self.snapshot()
        for path in [snapshot, snapshot/'managed', snapshot/'unbound', snapshot/runtime.MANIFEST]:
            self.assertEqual(path.stat().st_mode & 0o222, 0)
        commands = [
            "(p/'unbound').write_bytes(b'bad')",
            "(p/'managed/Unbound.Host.dll').unlink()",
            "(p/'managed/new.dll').write_bytes(b'bad')",
            "(p/'runtime-manifest.json').write_text('{}')",
        ]
        for command in commands:
            process = subprocess.run(['python3', '-c', f'from pathlib import Path; p=Path({str(snapshot)!r}); {command}'],
                                     capture_output=True, text=True)
            self.assertNotEqual(process.returncode, 0, command)
            self.assertIn('PermissionError', process.stderr)

    def test_simultaneous_processes_reuse_one_complete_snapshot(self):
        with concurrent.futures.ProcessPoolExecutor(max_workers=8) as pool:
            futures = [pool.submit(capture, self.cli, self.managed, self.root/f'run-{i}/runtime', self.cache)
                       for i in range(8)]
            paths = [future.result(timeout=30) for future in futures]
        self.assertEqual(len(set(paths)), 1)
        self.assertEqual(len([p for p in (self.cache/'snapshots').iterdir() if not p.name.startswith('.')]), 1)
        self.assertEqual(list((self.cache/'snapshots').glob('.tmp-*')), [])
        self.assertEqual(len(list((self.cache/'references').iterdir())), 8)

    def test_publication_hides_incomplete_files(self):
        copied, resume = threading.Event(), threading.Event()
        original = runtime.shutil.copyfile
        def blocked(source, destination):
            result = original(source, destination)
            if source == self.cli:
                copied.set()
                if not resume.wait(10):
                    raise RuntimeError('publication test timed out')
            return result
        with mock.patch.object(runtime.shutil, 'copyfile', side_effect=blocked):
            with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
                future = pool.submit(self.snapshot)
                try:
                    self.assertTrue(copied.wait(10))
                    self.assertFalse((self.root/'run/runtime').exists())
                    self.assertEqual([p for p in (self.cache/'snapshots').glob('*') if not p.name.startswith('.')], [])
                finally:
                    resume.set()
                snapshot = future.result(timeout=10)
        self.assertTrue((snapshot/runtime.MANIFEST).is_file())
        self.assertEqual((snapshot/'managed/nested/data').read_bytes(), b'input')

    def test_source_changes_abort_without_a_visible_snapshot(self):
        original = runtime.shutil.copyfile
        def changed(source, destination):
            if source == self.cli:
                source.write_bytes(b'changed while copying')
            return original(source, destination)
        with mock.patch.object(runtime.shutil, 'copyfile', side_effect=changed):
            with self.assertRaisesRegex(ValueError, 'changed while'):
                self.snapshot()
        self.assertEqual(list((self.cache/'snapshots').glob('*')), [])
        self.assertEqual(list((self.cache/'snapshots').glob('.tmp-*')), [])

    def test_corrupt_or_writable_snapshot_is_not_reused(self):
        first = self.snapshot()
        (first/'unbound').chmod(0o755)
        with self.assertRaisesRegex(ValueError, 'writable'):
            self.snapshot('other')
        (first/'unbound').write_bytes(b'bad')
        (first/'unbound').chmod(0o555)
        with self.assertRaisesRegex(ValueError, 'invalid snapshot file'):
            self.snapshot('corrupt')

    def test_old_unreferenced_snapshot_is_collected_but_live_run_is_preserved(self):
        first, live = self.snapshot(), self.snapshot('live')
        snapshot = first.resolve()
        first.unlink()
        os.utime(snapshot, (0, 0))
        self.assertEqual(runtime.cleanup(cache=self.cache, days=14), [])
        live.unlink()
        self.assertEqual(runtime.cleanup(cache=self.cache, days=14), [snapshot.name])
        self.assertFalse(snapshot.exists())
        self.assertEqual(list((self.cache/'references').iterdir()), [])

    def test_recent_unreferenced_snapshot_is_retained(self):
        first = self.snapshot()
        snapshot = first.resolve()
        first.unlink()
        self.assertEqual(runtime.cleanup(cache=self.cache), [])
        self.assertTrue(snapshot.is_dir())

    def test_main_pin_is_retained_until_a_new_main_build_is_pinned(self):
        repo = self.root/'repo'
        repo.mkdir()
        def git(*args):
            return subprocess.run(['git', '-C', str(repo), *args], check=True, capture_output=True, text=True).stdout.strip()
        git('init', '-b', 'main')
        git('-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '--allow-empty', '-m', 'initial')
        tree = git('rev-parse', 'HEAD^{tree}')
        first = self.snapshot()
        snapshot = first.resolve()
        runtime.pin_main(first, repo, tree, cache=self.cache)
        first.unlink()
        os.utime(snapshot, (0, 0))
        self.assertEqual(runtime.cleanup(cache=self.cache), [])
        (repo/'file').write_text('next tree')
        git('add', 'file')
        git('-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'next')
        self.assertEqual(runtime.cleanup(cache=self.cache), [])
        self.cli.write_bytes(b'next main build')
        latest = self.snapshot('next')
        runtime.pin_main(latest, repo, git('rev-parse', 'HEAD^{tree}'), cache=self.cache)
        self.assertEqual(runtime.cleanup(cache=self.cache), [snapshot.name])
        self.assertTrue(latest.is_dir())

    def test_docs_only_main_advance_keeps_the_previous_build_pinned(self):
        repo = self.root/'repo'
        repo.mkdir()
        def git(*args):
            return subprocess.run(['git', '-C', str(repo), *args], check=True, capture_output=True, text=True).stdout.strip()
        git('init', '-b', 'main')
        git('-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '--allow-empty', '-m', 'initial')
        previous = git('rev-parse', 'HEAD^{tree}')
        first = self.snapshot()
        snapshot = first.resolve()
        runtime.pin_main(first, repo, previous, cache=self.cache)
        first.unlink()
        os.utime(snapshot, (0, 0))
        (repo/'README.md').write_text('documentation')
        git('add', 'README.md')
        git('-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'docs')
        current = git('rev-parse', 'HEAD^{tree}')
        self.assertEqual(runtime.cleanup(cache=self.cache), [])
        runtime.pin_main(None, repo, current, previous_tree=previous, cache=self.cache)
        self.assertEqual(runtime.cleanup(cache=self.cache), [])
        self.assertTrue(snapshot.is_dir())

    def test_file_set_changes_during_capture_are_rejected(self):
        original = runtime.shutil.copyfile
        def changed(source, destination):
            (self.managed/'new.dll').write_bytes(b'new build input')
            return original(source, destination)
        with mock.patch.object(runtime.shutil, 'copyfile', side_effect=changed):
            with self.assertRaisesRegex(ValueError, 'file set changed'):
                self.snapshot()
        self.assertEqual(list((self.cache/'snapshots').glob('*')), [])

    def test_cleanup_removes_abandoned_staging_but_keeps_live_publishers(self):
        self.snapshot()
        abandoned = self.cache/'snapshots/.tmp-1073741824-old'
        live = self.cache/f'snapshots/.tmp-{os.getpid()}-live'
        for directory in (abandoned, live):
            directory.mkdir()
            (directory/'partial').write_bytes(b'partial capture')
            (directory/'partial').chmod(0o444)
            directory.chmod(0o555)
            os.utime(directory, (0, 0))
        self.assertEqual(runtime.cleanup(cache=self.cache), [abandoned.name])
        self.assertTrue(live.is_dir())

    def test_snapshot_storage_can_be_linked_to_another_disk(self):
        self.cache.mkdir()
        storage = self.root/'other-disk'
        storage.mkdir()
        (self.cache/'snapshots').symlink_to(storage, target_is_directory=True)
        first, second = self.snapshot(), self.snapshot('other')
        snapshot = first.resolve()
        self.assertEqual(snapshot.parent, storage)
        os.utime(snapshot, (0, 0))
        self.assertEqual(runtime.cleanup(cache=self.cache), [])
        first.unlink()
        second.unlink()
        self.assertEqual(runtime.cleanup(cache=self.cache), [snapshot.name])

    def test_cleanup_ignores_unrecognized_entries_and_directory_links(self):
        self.snapshot()
        foreign = self.cache/'snapshots/unrecognized'
        foreign.mkdir()
        (foreign/'keep').write_text('not a snapshot')
        os.utime(foreign, (0, 0))
        linked = self.cache/('snapshots/'+'a'*64)
        linked.symlink_to(foreign, target_is_directory=True)
        self.assertEqual(runtime.cleanup(cache=self.cache), [])
        self.assertTrue((foreign/'keep').is_file())
        self.assertTrue(linked.is_symlink())

    def test_explicit_probe_cli_keeps_its_adjacent_managed_artifacts(self):
        unrelated = self.root/'unrelated'
        unrelated.mkdir()
        with mock.patch.dict(os.environ, UNBOUND_MANAGED_DIR=str(unrelated)):
            self.assertEqual(runtime.managed_for(self.cli), unrelated)
            self.assertEqual(runtime.managed_for(self.cli, prefer_environment=False), self.managed)

    def test_existing_destination_is_never_replaced(self):
        first = self.snapshot()
        self.cli.write_bytes(b'another build')
        with self.assertRaisesRegex(ValueError, 'refusing to change'):
            self.snapshot()
        self.assertEqual((first/'unbound').read_bytes(), b'#!/bin/sh\nexit 0\n')

    def test_managed_content_is_hardlinked_between_snapshots_but_not_to_builds(self):
        first = self.snapshot()
        self.cli.write_bytes(b'next cli')
        second = self.snapshot('next')
        self.assertNotEqual(first.resolve(), second.resolve())
        a, b = first/'managed/Unbound.Host.dll', second/'managed/Unbound.Host.dll'
        self.assertEqual(a.stat().st_ino, b.stat().st_ino)
        self.assertNotEqual(a.stat().st_ino, (self.managed/'Unbound.Host.dll').stat().st_ino)
        (self.managed/'Unbound.Host.dll').chmod(0o755)
        executable = self.snapshot('executable')/'managed/Unbound.Host.dll'
        self.assertNotEqual(a.stat().st_ino, executable.stat().st_ino)
        self.assertEqual(executable.stat().st_mode & 0o777, 0o555)
        (self.managed/'Unbound.Host.dll').write_bytes(b'new managed')
        third = self.snapshot('third')
        self.assertNotEqual(a.stat().st_ino, (third/'managed/Unbound.Host.dll').stat().st_ino)
        self.assertEqual(a.read_bytes(), b'host')

    def test_managed_blob_alias_is_rejected_before_publication(self):
        self.snapshot()
        blob = next(p for p in (self.cache/'snapshots/.managed-blobs').iterdir() if p.read_bytes() == b'host')
        blob.unlink()
        source = self.managed/'Unbound.Host.dll'
        source.chmod(0o444)
        blob.symlink_to(source)
        self.cli.write_bytes(b'next cli')
        with self.assertRaisesRegex(ValueError, 'invalid managed blob'):
            self.snapshot('corrupt-blob')
        self.assertFalse((self.root/'corrupt-blob/runtime').exists())

    def test_task_release_keeps_other_tasks_and_promoted_runs(self):
        with mock.patch.object(runtime, 'task_owner', return_value={'worktree': str(self.root), 'branch': 'task'}):
            first = self.snapshot()
        self.cli.write_bytes(b'other task')
        with mock.patch.object(runtime, 'task_owner', return_value={'worktree': str(self.root), 'branch': 'other'}):
            other = self.snapshot('other')
        promoted = runtime.retain(first, self.root/'baseline/runtime', cache=self.cache)
        runtime.release_task(self.root, 'task', cache=self.cache)
        runtime.cleanup(cache=self.cache, days=0)
        self.assertFalse(first.is_symlink())
        self.assertTrue(promoted.exists() and other.exists())

    def test_task_finish_preserves_a_sheriff_candidate_for_later_promotion(self):
        import suite
        owner = {'worktree': str(self.root), 'branch': 'slot/sheriff-fixture'}
        with mock.patch.object(runtime, 'task_owner', return_value=owner):
            candidate = self.snapshot('validation/suite/sheriff-candidate')
            snapshot = candidate.resolve()
            self.cli.write_bytes(b'ordinary task runtime')
            ordinary = self.snapshot('validation/suite/ordinary-control')
        for reference in (candidate, ordinary):
            os.utime(reference.resolve(), (0, 0))
        with mock.patch.dict(os.environ, UNBOUND_RUNTIME_CACHE=str(self.cache)):
            runtime.finish_task(self.root, owner['branch'])
        self.assertTrue(candidate.exists())
        self.assertFalse(ordinary.is_symlink())
        agents = self.root/'agents'
        summary = agents/'suite/sheriff-candidate/summary.json'
        summary.parent.mkdir(parents=True)
        summary.write_text(json.dumps({'work': str(candidate.parent), 'runtime_snapshot': str(snapshot)}))
        with mock.patch.object(suite, 'AGENTS', agents), mock.patch.dict(os.environ, UNBOUND_RUNTIME_CACHE=str(self.cache)):
            suite.promote(summary)
        runtime.release_reference(candidate, cache=self.cache)
        runtime.cleanup(cache=self.cache, days=0)
        self.assertTrue((summary.parent/'runtime').exists())
        self.assertEqual((agents/'baseline/runtime').resolve(), snapshot)

    def test_main_suite_automatic_release_keeps_sheriff_until_explicit_dismissal(self):
        import suite
        owner = {'worktree': str(self.root), 'branch': 'main', 'pid': 1073741824, 'start': 'exited'}
        agents, outputs = self.root/'agents', self.root/'validation-cache'
        with mock.patch.object(suite, 'AGENTS', agents), mock.patch.object(suite, 'CACHE', outputs), \
             mock.patch.object(suite, 'managed_for', return_value=self.managed), \
             mock.patch.object(suite, 'discover', return_value=['fixture']), \
             mock.patch.object(suite, 'recipe_for', return_value=[suite.Step('compare', ['true'])]), \
             mock.patch.object(suite, 'task_owner', return_value=owner), \
             mock.patch.object(runtime, 'task_owner', return_value=owner), \
             mock.patch.dict(os.environ, UNBOUND_RUNTIME_CACHE=str(self.cache)):
            for label in ('sheriff-candidate', 'ordinary-control'):
                self.cli.write_bytes(label.encode())
                with mock.patch('sys.argv', ['suite.py', '--cli', str(self.cli), '--label', label, '--against', 'none']):
                    self.assertEqual(suite.main(), 0)
        candidate = outputs/'validation/suite/sheriff-candidate/runtime'
        ordinary = outputs/'validation/suite/ordinary-control/runtime'
        self.assertTrue(candidate.exists())
        self.assertFalse(ordinary.is_symlink())
        snapshot = candidate.resolve()
        runtime.release_reference(candidate, cache=self.cache)
        runtime.cleanup(cache=self.cache, days=0)
        self.assertFalse(candidate.is_symlink())
        self.assertFalse(snapshot.exists())

    def test_pending_release_waits_for_live_owner_and_active_engine(self):
        with mock.patch.object(runtime, 'task_owner', return_value={
                'worktree': str(self.root), 'branch': 'task', 'pid': 123, 'start': '456'}):
            first = self.snapshot()
        snapshot = first.resolve()
        runtime.release_task(self.root, 'task', cache=self.cache)
        with mock.patch.object(runtime, 'process_start', return_value='456'):
            runtime.cleanup(cache=self.cache, days=0)
        self.assertTrue(first.exists())
        with mock.patch.object(runtime, 'process_start', return_value=None), \
             mock.patch.object(runtime, 'active_snapshots', return_value={snapshot.name}):
            runtime.cleanup(cache=self.cache, days=0)
        self.assertTrue(first.exists())
        with mock.patch.object(runtime, 'process_start', return_value='different-start'):
            self.assertIn(snapshot.name, runtime.cleanup(cache=self.cache, days=0))
        self.assertFalse(first.is_symlink())

    def test_legacy_baseline_and_read_only_snapshot_collection(self):
        first = self.snapshot()
        snapshot = first.resolve()
        baseline = self.cache.parent/'agents/baseline'
        baseline.mkdir(parents=True)
        (baseline/'summary.json').write_text(json.dumps({'cli': str(first/'unbound')}))
        runtime.release_reference(first, cache=self.cache)
        os.utime(snapshot, (0, 0))
        self.assertEqual(runtime.cleanup(cache=self.cache, days=0), [])
        self.assertTrue(first.is_symlink())
        (baseline/'summary.json').unlink()
        self.assertEqual(runtime.cleanup(cache=self.cache, days=0), [snapshot.name])
        self.assertFalse(snapshot.exists())
        self.assertEqual(list((self.cache/'snapshots/.managed-blobs').iterdir()), [])

    def test_actual_live_process_keeps_a_released_snapshot(self):
        first = self.snapshot()
        snapshot = first.resolve()
        child = subprocess.Popen(['python3', '-c', 'import time;time.sleep(60)'],
                                 env=dict(os.environ, UNBOUND_RUNTIME=str(first)))
        try:
            runtime.release_reference(first, cache=self.cache)
            self.assertIn(snapshot.name, runtime.active_snapshots(self.cache))
            self.assertEqual(runtime.cleanup(cache=self.cache, days=0), [])
            self.assertTrue(first.exists())
        finally:
            child.terminate()
            child.wait(timeout=5)
        self.assertIn(snapshot.name, runtime.cleanup(cache=self.cache, days=0))

    def test_promoting_a_completed_suite_restores_its_reference_and_retains_older_promotions(self):
        import suite
        first = self.snapshot()
        snapshot = first.resolve()
        runtime.release_reference(first, cache=self.cache)
        runtime.cleanup(cache=self.cache, days=14)
        self.assertFalse(first.is_symlink())
        agents = self.root/'agents'
        summary = agents/'suite/first/summary.json'
        summary.parent.mkdir(parents=True)
        summary.write_text(json.dumps({'work': str(first.parent), 'runtime_snapshot': str(snapshot)}))
        with mock.patch.object(suite, 'AGENTS', agents), mock.patch.dict(os.environ, UNBOUND_RUNTIME_CACHE=str(self.cache)):
            suite.promote(summary)
            self.assertEqual((agents/'baseline/runtime').resolve(), snapshot)
            self.cli.write_bytes(b'another release')
            second = self.snapshot('next')
            next_summary = agents/'suite/second/summary.json'
            next_summary.parent.mkdir()
            next_summary.write_text(json.dumps({'work': str(second.parent)}))
            suite.promote(next_summary)
            runtime.release_reference(second, cache=self.cache)
            runtime.cleanup(cache=self.cache, days=0)
        self.assertTrue(snapshot.exists())
        self.assertTrue((summary.parent/'runtime').exists())
        self.assertEqual((agents/'baseline/runtime').resolve(), (next_summary.parent/'runtime').resolve())

    def test_queued_command_argument_keeps_a_released_snapshot(self):
        first = self.snapshot()
        snapshot = first.resolve()
        child = subprocess.Popen(['python3', '-c', 'import time;time.sleep(60)', str(first/'unbound')])
        try:
            runtime.release_reference(first, cache=self.cache)
            self.assertIn(snapshot.name, runtime.active_snapshots(self.cache))
            self.assertEqual(runtime.cleanup(cache=self.cache, days=0), [])
        finally:
            child.terminate()
            child.wait(timeout=5)
        self.assertIn(snapshot.name, runtime.cleanup(cache=self.cache, days=0))

    def test_live_wrapper_in_a_run_keeps_its_relative_runtime(self):
        first = self.snapshot()
        snapshot = first.resolve()
        project = first.parent/'project'
        project.mkdir()
        child = subprocess.Popen(['python3', '-c', 'import time;time.sleep(60)'], cwd=project)
        try:
            runtime.release_reference(first, cache=self.cache)
            self.assertIn(snapshot.name, runtime.active_snapshots(self.cache))
            self.assertEqual(runtime.cleanup(cache=self.cache, days=0), [])
        finally:
            child.terminate()
            child.wait(timeout=5)
        self.assertIn(snapshot.name, runtime.cleanup(cache=self.cache, days=0))

    def test_collection_racing_promotion_preserves_the_existing_baseline(self):
        import suite
        first = self.snapshot()
        candidate = first.resolve()
        self.cli.write_bytes(b'previous baseline')
        old = self.snapshot('old')
        agents = self.root/'agents'
        baseline = agents/'baseline'
        runtime.retain(old, baseline/'runtime', cache=self.cache)
        (baseline/'summary.json').write_text('{"previous":true}\n')
        summary = agents/'suite/candidate/summary.json'
        summary.parent.mkdir(parents=True)
        summary.write_text(json.dumps({'work': str(first.parent), 'runtime_snapshot': str(candidate)}))
        runtime.release_reference(first, cache=self.cache)
        runtime.cleanup(cache=self.cache, days=14)
        self.assertFalse(first.is_symlink())
        original = runtime.cache_lock
        @contextlib.contextmanager
        def collect_before_acquiring(cache):
            with mock.patch.object(runtime, 'cache_lock', original):
                runtime.cleanup(cache=self.cache, days=0)
            with original(cache):
                yield
        with mock.patch.object(suite, 'AGENTS', agents), \
             mock.patch.dict(os.environ, UNBOUND_RUNTIME_CACHE=str(self.cache)), \
             mock.patch.object(runtime, 'cache_lock', collect_before_acquiring):
            with self.assertRaisesRegex(ValueError, 'collected before promotion'):
                suite.promote(summary)
        self.assertFalse(candidate.exists())
        self.assertEqual((baseline/'runtime').resolve(), old.resolve())
        self.assertEqual(json.loads((baseline/'summary.json').read_text()), {'previous': True})

    def test_legacy_suite_reference_without_promotion_history_is_preserved(self):
        reference = runtime.snapshot_runtime(self.cli, self.managed,
                                             self.root/'validation/suite/legacy/runtime', cache=self.cache)
        for path in (self.cache/'references').iterdir():
            record = json.loads(path.read_text())
            record.pop('owner', None)
            record.pop('retained', None)
            runtime.atomic_json(path, record)
        summary = self.root/'agents/suite/legacy/summary.json'
        summary.parent.mkdir(parents=True)
        summary.write_text(json.dumps({'commit': 'completed-commit', 'work': str(reference.parent)}))
        with mock.patch.object(runtime.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0)):
            self.assertEqual(runtime.cleanup(cache=self.cache, days=0, repo=self.root), [])
        self.assertTrue(reference.exists())

    def test_recapturing_identical_content_does_not_unprotect_a_promoted_reference(self):
        first = self.snapshot()
        promoted = runtime.retain(first, self.root/'promoted/runtime', cache=self.cache)
        runtime.snapshot_runtime(self.cli, self.managed, promoted, cache=self.cache)
        runtime.release_reference(promoted, cache=self.cache)
        first.unlink()
        self.assertEqual(runtime.cleanup(cache=self.cache, days=0), [])
        self.assertTrue(promoted.exists())

    def test_promoting_an_already_collected_runtime_preserves_the_baseline(self):
        import suite
        first = self.snapshot()
        candidate = first.resolve()
        runtime.release_reference(first, cache=self.cache)
        runtime.cleanup(cache=self.cache, days=0)
        self.assertFalse(candidate.exists())
        self.cli.write_bytes(b'previous baseline')
        old = self.snapshot('old')
        agents = self.root/'agents'
        baseline = agents/'baseline'
        runtime.retain(old, baseline/'runtime', cache=self.cache)
        (baseline/'summary.json').write_text('{"previous":true}\n')
        summary = agents/'suite/missing/summary.json'
        summary.parent.mkdir(parents=True)
        summary.write_text(json.dumps({'work': str(first.parent), 'runtime_snapshot': str(candidate)}))
        with mock.patch.object(suite, 'AGENTS', agents), mock.patch.dict(os.environ, UNBOUND_RUNTIME_CACHE=str(self.cache)):
            with self.assertRaisesRegex(ValueError, 'recorded runtime is no longer available'):
                suite.promote(summary)
        self.assertEqual((baseline/'runtime').resolve(), old.resolve())
        self.assertEqual(json.loads((baseline/'summary.json').read_text()), {'previous': True})


if __name__ == '__main__':
    unittest.main()
