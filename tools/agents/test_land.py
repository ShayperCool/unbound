#!/usr/bin/env python3
"""Touched-crate coverage for the serialized landing gate."""
from pathlib import Path
import contextlib
import fcntl
import io
import json
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock
import os

sys.path.insert(0, str(Path(__file__).resolve().parent))
import land


class TouchedCratesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        source = Path(__file__).resolve().parents[2]
        # Use the real native build recipes and importer dependencies so the
        # model-cache guard cannot silently stop being selected as they evolve.
        for name in ['unbound-assets', 'unbound-host']:
            for file in ['Cargo.toml', 'build.rs']:
                self.write(f'crates/{name}/{file}', (source/'crates'/name/file).read_text())
        self.write('crates/core/Cargo.toml', '[package]\nname = "unbound-core"\nversion = "0.1.0"\n')
        self.write('Cargo.toml', '[workspace]\nmembers = ["crates/*"]\n')
        self.write('Cargo.lock', 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.0"\n')
        self.git('init', '-q')
        self.git('config', 'user.email', 'infra-test@example.invalid')
        self.git('config', 'user.name', 'Infra test')
        self.git('config', 'core.hooksPath', '/dev/null')
        self.commit()
        self.base = self.git('rev-parse', 'HEAD')
        self.gate = land.Gate(self.root, self.base, [], False, self.root/'gate.log')

    def write(self, path, text):
        target = self.root/path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)

    def git(self, *args):
        return subprocess.run(['git', *args], cwd=self.root, check=True,
                              capture_output=True, text=True).stdout.strip()

    def commit(self):
        self.git('add', '.')
        self.git('commit', '-qm', 'test(infra): selection fixture')

    def test_rust_sources_select_manifest_names_and_deduplicate(self):
        self.assertEqual(self.gate.touched_crates([
            'crates/core/src/lib.rs', 'crates/core/src/other.rs',
            'crates/unbound-assets/src/model/mod.rs']), ['unbound-assets', 'unbound-core'])

    def test_native_sources_select_their_build_crates(self):
        for path, expected in [
            ('native/glu_tess/src/tess.c', ['unbound-assets']),
            ('native/unbound_model_tess.c', ['unbound-assets']),
            ('native/vorbis/lib/block.c', ['unbound-assets']),
            ('native/unbound_physx.cpp', ['unbound-host']),
            ('native/physx/source/file.cpp', ['unbound-host']),
            ('native/unrelated/file.c', []),
        ]:
            with self.subTest(path=path):
                self.assertEqual(self.gate.touched_crates([path]), expected)

    def test_locked_importer_dependency_bump_selects_model_guard(self):
        self.write('Cargo.lock', 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.1"\n')
        self.commit()
        self.assertEqual(self.gate.touched_crates(['Cargo.lock']), ['unbound-assets'])

    def test_unrelated_lock_change_does_not_select_importer(self):
        self.write('Cargo.lock', 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.0"\n'
                   + '\n[[package]]\nname = "unrelated"\nversion = "1.0.0"\n')
        self.commit()
        self.assertEqual(self.gate.touched_crates(['Cargo.lock']), [])

    def test_transitive_dependency_change_selects_importer(self):
        dependency = '\n[[package]]\nname = "decoder"\nversion = "1.0.0"\nchecksum = "aaa"\n'
        importer = 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.0"\ndependencies = ["decoder"]\n'
        self.write('Cargo.lock', importer+dependency)
        self.commit()
        self.gate.base = self.git('rev-parse', 'HEAD')
        self.write('Cargo.lock', importer+dependency.replace('aaa', 'bbb'))
        self.commit()
        self.assertEqual(self.gate.touched_crates(['Cargo.lock']), ['unbound-assets'])

    def test_renamed_target_dependency_selects_consumer(self):
        self.write('crates/core/Cargo.toml', '[package]\nname = "unbound-core"\nversion = "0.1.0"\n'
                   + '[target.\'cfg(unix)\'.build-dependencies]\nalias = { package = "ufbx", version = "0.11" }\n')
        self.write('Cargo.lock', 'version = 4\n\n[[package]]\nname = "ufbx"\nversion = "0.11.1"\n')
        self.commit()
        self.assertEqual(self.gate.touched_crates(['Cargo.lock']), ['unbound-assets', 'unbound-core'])

    def test_crate_manifest_selects_its_crate(self):
        self.assertEqual(self.gate.touched_crates(['crates/unbound-assets/Cargo.toml']), ['unbound-assets'])

    def test_workspace_manifest_selects_all_crates(self):
        self.assertEqual(self.gate.touched_crates(['Cargo.toml']),
                         ['unbound-assets', 'unbound-core', 'unbound-host'])

    def test_docs_only_select_no_crates(self):
        self.assertEqual(self.gate.touched_crates(['README.md', 'docs/ARCHITECTURE.md']), [])

    def test_managed_only_select_no_crates(self):
        self.assertEqual(self.gate.touched_crates(['managed/UnityEngine/Object.cs']), [])

    def test_probe_inputs_select_crates_whose_tests_read_them(self):
        # The real model output guard, so its probe list cannot silently stop selecting it.
        source = Path(__file__).resolve().parents[2]
        guard = 'crates/unbound-assets/src/model/version_guard.rs'
        self.write(guard, (source/guard).read_text())
        self.write('crates/core/src/lib.rs',
                   'const R: &str = include_str!("../../../tests/projects/sample/Reference/a");\n')
        for path, expected in [
            ('tests/projects/humanoid/Assets/Resources/HumanA.fbx.meta', ['unbound-assets']),
            ('tests/projects/modelimport/Assets/Models/Cube.fbx', ['unbound-assets']),
            ('tests/projects/sample/README.md', ['unbound-core']),
            ('tests/projects/sampled/README.md', []),
            ('tests/projects/unreferenced/Assets/Probe.cs', []),
            ('tests/projects/README.md', []),
        ]:
            with self.subTest(path=path):
                self.assertEqual(self.gate.touched_crates([path]), expected)

    def test_deleted_crate_is_not_tested(self):
        self.assertEqual(self.gate.touched_crates(['crates/removed/src/lib.rs']), [])


class GatePriorityTests(unittest.TestCase):
    def test_only_lock_holding_gates_mark_all_their_child_jobs(self):
        root = Path('/tmp/priority-control')
        with mock.patch.dict(os.environ, {'UNBOUND_LAND_GATE': '1'}):
            check = land.Gate(root, 'HEAD', [], False, root/'check.log')
            gate = land.Gate(root, 'HEAD', [], False, root/'land.log', priority=True)
        self.assertNotIn('UNBOUND_LAND_GATE', check.env)
        self.assertEqual(gate.env['UNBOUND_LAND_GATE'], '1')

    def test_environment_discovery_preserves_the_gate_priority_choice(self):
        class StopBuild(Exception):
            pass
        for priority in (False, True):
            with self.subTest(priority=priority), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                with mock.patch.dict(os.environ, {'UNBOUND_LAND_GATE': '1'}):
                    gate = land.Gate(root, 'main', [], False, root/'gate.log', priority=priority)
                    def run(command, **kwargs):
                        selected = {'CARGO_TARGET_DIR': str(root/'target')}
                        flag = kwargs.get('env', os.environ).get('UNBOUND_LAND_GATE')
                        if flag is not None:
                            selected['UNBOUND_LAND_GATE'] = flag
                        return subprocess.CompletedProcess(command, 0, json.dumps(selected))
                    with mock.patch.object(land, 'git', return_value='tools/agents/land.py'), \
                         mock.patch.object(land.subprocess, 'run', side_effect=run), \
                         mock.patch.object(gate, 'step', return_value=1), \
                         mock.patch.object(gate, 'fail', side_effect=StopBuild), \
                         contextlib.redirect_stdout(io.StringIO()):
                        with self.assertRaises(StopBuild):
                            gate.run()
                self.assertEqual(gate.env.get('UNBOUND_LAND_GATE'), '1' if priority else None)


class GateCacheTests(unittest.TestCase):
    def test_base_and_union_coverage_change_the_pass_key(self):
        root = Path('/tmp/train-key-control')
        options = dict(base='base', changed=['crates/alpha/src/lib.rs'], crates=['alpha'])
        original = land.stamp_path('tree', [], False, root, **options)
        for key, value in [('base', 'other'), ('changed', ['managed/Control.cs']), ('crates', ['beta'])]:
            changed = dict(options, **{key: value})
            with self.subTest(key=key):
                self.assertNotEqual(original, land.stamp_path('tree', [], False, root, **changed))


TRAIN_CALLER = r'''
import json, os, sys, time
from pathlib import Path
sys.path.insert(0, sys.argv[1])
import land, land_train
land.STATE = Path(sys.argv[2])
land.integration_root = lambda: land.STATE/'integration'
land_train.POLL_SECONDS = .02
events = Path(sys.argv[3])
def event(value):
    with events.open('a') as stream:
        stream.write(json.dumps(value)+'\n')
def gate(root, base, probes, full, branch, **options):
    values = {path.name: path.read_text().strip() for path in root.glob('*.txt')}
    event({'gate': values, 'probes': probes, 'crates': options['crates'],
           'changed': options['changed'], 'priority': options['priority']})
    log = options['log']
    log.write_text('fixture gate: '+json.dumps(values)+'\n')
    marker = land.STATE/'gate-running'
    marker.write_text('running')
    if (land.STATE/'pause-gate').exists():
        deadline = time.monotonic()+10
        while (land.STATE/'pause-gate').exists() and time.monotonic()<deadline:
            time.sleep(.02)
    if values.get('bad.txt') == 'fail' or ('probe-b' in probes and values.get('bad-probe.txt') == 'fail') or ('probe-a' in probes and (values.get('a.txt') != 'ok' or values.get('break-a.txt') == 'yes')):
        raise SystemExit(5)
    return None
land.run_gate = gate
land.pin_main = lambda *args, **kwargs: None
land.push_main = lambda *args: event({'push': True})
if os.environ.get('TRAIN_CRASH_AFTER_ADVANCE'):
    def crash(*args, **kwargs):
        os._exit(73)
    land_train.LandTrain.record = crash
sys.argv = ['land.py', *sys.argv[4:]]
raise SystemExit(land.main())
'''


class TrainIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root/'repo'
        self.repo.mkdir()
        self.state = self.root/'state'
        self.state.mkdir()
        self.events = self.root/'events.jsonl'
        self.caller = self.root/'caller.py'
        self.caller.write_text(TRAIN_CALLER)
        self.children = []
        self.addCleanup(self.stop_children)
        self.git('init', '-q', '-b', 'main')
        self.git('config', 'user.name', 'Train Control')
        self.git('config', 'user.email', 'train@example.invalid')
        self.git('config', 'core.hooksPath', '/dev/null')
        (self.repo/'shared.txt').write_text('base\n')
        for name in ('alpha', 'beta', 'gamma'):
            path = self.repo/'crates'/name
            (path/'src').mkdir(parents=True)
            (path/'Cargo.toml').write_text(f'[package]\nname = "{name}"\nversion = "0.1.0"\n')
            (path/'src/lib.rs').write_text('// base\n')
        reference = self.repo/'tests/projects/control/Reference'
        reference.mkdir(parents=True)
        for name in ('a', 'b'):
            (reference/(name+'.json')).write_text('{"value":0}\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): train base')

    def stop_children(self):
        for process, stream in self.children:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=5)
            stream.close()

    def git(self, *args, cwd=None):
        return subprocess.run(['git', *args], cwd=cwd or self.repo, check=True,
                              capture_output=True, text=True).stdout.strip()

    def branch(self, name, files, base='main', message=None):
        path = self.root/name
        self.git('worktree', 'add', '-q', '-b', name, str(path), base)
        for file, text in files.items():
            target = path/file
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(text)
        self.git('add', '.', cwd=path)
        self.git('commit', '-qm', message or f'test(agents): train {name}', cwd=path)
        return path

    def start(self, path, probes=(), *, crash=False):
        log = self.root/(path.name+'.log')
        stream = log.open('w')
        env = dict(os.environ, UNBOUND_LAND_NO_PUSH='1')
        env.pop('TRAIN_CRASH_AFTER_ADVANCE', None)
        if crash:
            env['TRAIN_CRASH_AFTER_ADVANCE'] = '1'
        command = [sys.executable, str(self.caller), str(Path(land.__file__).parent),
                   str(self.state), str(self.events)]
        for probe in probes:
            command += ['--probe', probe]
        process = subprocess.Popen(command, cwd=path, env=env, stdout=stream,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        self.children.append((process, stream))
        return process, log

    def wait_for(self, predicate, message):
        deadline = time.monotonic()+15
        while time.monotonic()<deadline:
            if predicate():
                return
            time.sleep(.02)
        self.fail(message+'\n'+ '\n'.join(path.read_text() for path in self.root.glob('*.log')))

    def registered(self, count):
        def ready():
            path = self.state/'land-queue.json'
            return path.exists() and len(json.loads(path.read_text())['entries']) == count
        self.wait_for(ready, f'{count} callers did not register')

    def finish(self, workers):
        results = []
        for process, log in workers:
            try:
                code = process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                self.fail('caller stalled:\n'+log.read_text())
            results.append((code, log.read_text()))
        return results

    def rows(self):
        path = self.state/'landed.jsonl'
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def gates(self):
        return [json.loads(line) for line in self.events.read_text().splitlines() if '"gate"' in line]

    @contextlib.contextmanager
    def hold(self):
        with (self.state/'land.lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            yield

    def test_all_branches_share_one_gate_and_one_push(self):
        first = self.branch('first', {'a.txt': 'ok', 'crates/alpha/src/lib.rs': '// changed\n'})
        second = self.branch('second', {'b.txt': 'ok', 'crates/beta/src/lib.rs': '// changed\n',
                                        'crates/alpha/src/lib.rs': '// base\n'}, base='first')
        third = self.branch('third', {'c.txt': 'ok', 'crates/gamma/src/lib.rs': '// changed\n'})
        with self.hold():
            workers = []
            for count, path in enumerate((first, second, third), 1):
                workers.append(self.start(path, ['probe-a'] if count == 1 else ['probe-common']))
                self.registered(count)
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 0, 0], results)
        gates = self.gates()
        self.assertEqual(len(gates), 1)
        self.assertEqual(gates[0]['crates'], ['alpha', 'beta', 'gamma'])
        self.assertEqual(gates[0]['probes'], ['probe-a', 'probe-common'])
        self.assertTrue(gates[0]['priority'])
        rows = self.rows()
        self.assertEqual([row['branch'] for row in rows], ['first', 'second', 'third'])
        self.assertEqual(len({row['train_id'] for row in rows}), 1)
        self.assertEqual(sum('"push"' in line for line in self.events.read_text().splitlines()), 1)
        for path in (first, second, third):
            head = self.git('rev-parse', 'HEAD', cwd=path)
            self.git('merge-base', '--is-ancestor', head, 'main')
            self.assertEqual(self.git('status', '--porcelain', cwd=path), '')

    def test_failed_middle_branch_does_not_block_independent_later_branch(self):
        paths = [self.branch('first', {'a.txt': 'ok'}),
                 self.branch('bad', {'bad.txt': 'fail'}), self.branch('last', {'c.txt': 'ok'})]
        with self.hold():
            workers = []
            for count, path in enumerate(paths, 1):
                workers.append(self.start(path, ['probe-a'] if count == 1 else []))
                self.registered(count)
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 5, 0], results)
        self.assertEqual([row['branch'] for row in self.rows()], ['first', 'last'])
        self.assertNotIn('bad.txt', self.git('ls-tree', '--name-only', 'main'))
        self.assertIn('full log:', results[1][1])
        self.assertLessEqual(len(self.gates()), 5)

    def test_conflict_drops_only_its_branch(self):
        paths = [self.branch('first', {'shared.txt': 'first'}),
                 self.branch('conflict', {'shared.txt': 'conflict'}),
                 self.branch('last', {'c.txt': 'ok'})]
        original = self.git('rev-parse', 'HEAD', cwd=paths[1])
        with self.hold():
            workers = []
            for count, path in enumerate(paths, 1):
                workers.append(self.start(path))
                self.registered(count)
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 3, 0], results)
        self.assertEqual([row['branch'] for row in self.rows()], ['first', 'last'])
        self.assertEqual(len(self.gates()), 1)
        self.assertEqual(self.git('rev-parse', 'HEAD', cwd=paths[1]), original)
        self.assertIn('conflicts with main', results[1][1])

    def test_later_gates_retain_checks_from_the_landed_prefix(self):
        paths = [self.branch('first', {'a.txt': 'ok'}), self.branch('bad', {'bad.txt': 'fail'}),
                 self.branch('last', {'break-a.txt': 'yes'})]
        with self.hold():
            workers = []
            for count, path in enumerate(paths, 1):
                workers.append(self.start(path, ['probe-a'] if count == 1 else []))
                self.registered(count)
        results = self.finish(workers)
        self.assertEqual(results[0][0], 0, results)
        self.assertEqual(results[1][0], 5, results)
        self.assertEqual(results[2][0], 5, results)
        self.assertEqual([row['branch'] for row in self.rows()], ['first'])

    def test_one_branch_cannot_authorize_another_branchs_oracle_edit(self):
        first = self.branch('first', {'tests/projects/control/Reference/a.json': '{"value":1}\n'},
                            message='test(agents): approved fixture\n\nOracle-Change: controlled test fixture')
        second = self.branch('second', {'tests/projects/control/Reference/b.json': '{"value":2}\n'})
        with self.hold():
            workers = [self.start(first)]
            self.registered(1)
            workers.append(self.start(second))
            self.registered(2)
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 4], results)
        self.assertEqual([row['branch'] for row in self.rows()], ['first'])
        self.assertEqual(len(self.gates()), 1)

    def test_rejected_ancestor_checks_follow_its_commits_in_a_stacked_branch(self):
        first = self.branch('first', {'a.txt': 'ok'})
        bad = self.branch('bad', {'bad-probe.txt': 'fail'})
        last = self.branch('last', {'c.txt': 'ok'}, base='bad')
        with self.hold():
            workers = []
            for count, path in enumerate((first, bad, last), 1):
                workers.append(self.start(path, ['probe-b'] if count == 2 else []))
                self.registered(count)
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 5, 5], results)
        self.assertEqual([row['branch'] for row in self.rows()], ['first'])

    def test_work_added_while_gating_is_retained_and_the_branch_is_rejected(self):
        first = self.branch('first', {'a.txt': 'ok'})
        second = self.branch('second', {'b.txt': 'ok'})
        pause = self.state/'pause-gate'
        pause.write_text('pause')
        with self.hold():
            workers = [self.start(first)]
            self.registered(1)
            workers.append(self.start(second))
            self.registered(2)
        self.wait_for(lambda: (self.state/'gate-running').exists(), 'gate did not start')
        (second/'keep.txt').write_text('uncommitted work\n')
        pause.unlink()
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 2], results)
        self.assertEqual((second/'keep.txt').read_text(), 'uncommitted work\n')
        self.assertEqual([row['branch'] for row in self.rows()], ['first'])

    def test_another_caller_recovers_an_advance_before_its_result_was_written(self):
        first = self.branch('first', {'a.txt': 'ok'})
        second = self.branch('second', {'b.txt': 'ok'})
        pause = self.state/'pause-gate'
        pause.write_text('pause')
        workers = [self.start(first, crash=True)]
        self.wait_for(lambda: (self.state/'gate-running').exists(), 'first gate did not start')
        workers.append(self.start(second))
        self.registered(2)
        pause.unlink()
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [73, 0], results)
        self.assertEqual({row['branch'] for row in self.rows()}, {'first', 'second'})
        self.assertEqual(len(self.rows()), 2)

    def test_cancelled_waiter_is_removed_without_landing_its_branch(self):
        first = self.branch('first', {'a.txt': 'ok'})
        second = self.branch('second', {'b.txt': 'ok'})
        with self.hold():
            workers = [self.start(first)]
            self.registered(1)
            workers.append(self.start(second))
            self.registered(2)
            workers[0][0].terminate()
            workers[0][0].wait(timeout=5)
        results = self.finish(workers)
        self.assertEqual(results[1][0], 0, results)
        self.assertEqual([row['branch'] for row in self.rows()], ['second'])
        self.assertEqual(len(self.gates()), 1)
        self.assertNotIn('a.txt', self.git('ls-tree', '--name-only', 'main'))



if __name__ == '__main__':
    unittest.main()
