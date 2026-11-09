#!/usr/bin/env python3
"""Touched-crate coverage for the serialized landing gate."""
from pathlib import Path
import contextlib
import fcntl
import io
import json
import signal
import shlex
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
import contextlib, json, os, sys, time
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
           'changed': options['changed'], 'priority': options['priority'],
           'automatic': options.get('automatic', [])})
    log = options['log']
    log.write_text('fixture gate: '+json.dumps(values)+'\n')
    marker = land.STATE/'gate-running'
    marker.write_text('running')
    if (land.STATE/'pause-gate').exists():
        deadline = time.monotonic()+10
        while (land.STATE/'pause-gate').exists() and time.monotonic()<deadline:
            time.sleep(.02)
    automatic_left = any(row[0] == 'left' and 'tools/agents/suite.py' in row[1]
                         for row in options.get('automatic', []))
    if (automatic_left and values.get('break-left.txt') == 'yes') or values.get('bad.txt') == 'fail' or ('probe-b' in probes and values.get('bad-probe.txt') == 'fail') or ('probe-a' in probes and (values.get('a.txt') != 'ok' or values.get('break-a.txt') == 'yes')):
        raise SystemExit(5)
    return None
if os.environ.get('TRAIN_LEGACY_REQUEST'):
    original_register = land_train.Queue.register
    @contextlib.contextmanager
    def legacy_register(self, entry):
        entry = dict(entry)
        entry.pop('automatic', None)
        entry.pop('probe_environment', None)
        with original_register(self, entry) as value:
            yield value
    land_train.Queue.register = legacy_register
land.run_gate = gate
land.pin_main = lambda *args, **kwargs: None
land.push_main = lambda *args: event({'push': True})
if os.environ.get('TRAIN_CRASH_AFTER_ADVANCE'):
    def crash(*args, **kwargs):
        os._exit(73)
    land_train.LandTrain.record = crash
if os.environ.get('TRAIN_PAUSE_LOCK'):
    original_lock = land.land_lock
    @contextlib.contextmanager
    def paused_lock(*args, **kwargs):
        (land.STATE/'before-lock').write_text('waiting')
        while (land.STATE/'pause-lock').exists():
            time.sleep(.02)
        with original_lock(*args, **kwargs) as value:
            yield value
    land.land_lock = paused_lock
if os.environ.get('TRAIN_PAUSE_LEADER'):
    original_lead = land_train.LandTrain.lead
    def paused_lead(self):
        (land.STATE/'leader-running').write_text('waiting')
        while (land.STATE/'pause-leader').exists():
            time.sleep(.02)
        return original_lead(self)
    land_train.LandTrain.lead = paused_lead
if any(os.environ.get(key) for key in ('TRAIN_CRASH_FINAL', 'TRAIN_CRASH_PREFIX', 'TRAIN_CRASH_AFTER_FINAL_RECORD')):
    original_record = land_train.LandTrain.record
    def crash_record(self, entry, result):
        if os.environ.get('TRAIN_CRASH_FINAL') and entry['branch'] == 'second':
            os._exit(73)
        original_record(self, entry, result)
        if ((os.environ.get('TRAIN_CRASH_PREFIX') and entry['branch'] == 'first') or
                (os.environ.get('TRAIN_CRASH_AFTER_FINAL_RECORD') and entry['branch'] == 'second')):
            os._exit(73)
    land_train.LandTrain.record = crash_record
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
        (reference.parent/'gate.json').write_text(json.dumps({'commands': ['true']}))
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

    def start(self, path, probes=(), *, crash=False, pause_lock=False, pause_leader=False,
              crash_final=False, crash_prefix=False, crash_after_final_record=False, probe_release=None, legacy=False):
        log = self.root/(path.name+'.log')
        stream = log.open('w')
        env = dict(os.environ, UNBOUND_LAND_NO_PUSH='1')
        if probe_release is not None:
            env[land.RELEASE_VARIABLE] = probe_release
        settings = {'TRAIN_LEGACY_REQUEST': legacy, 'TRAIN_CRASH_AFTER_ADVANCE': crash, 'TRAIN_PAUSE_LOCK': pause_lock,
                    'TRAIN_PAUSE_LEADER': pause_leader, 'TRAIN_CRASH_FINAL': crash_final,
                    'TRAIN_CRASH_PREFIX': crash_prefix,
                    'TRAIN_CRASH_AFTER_FINAL_RECORD': crash_after_final_record}
        for key, enabled in settings.items():
            env.pop(key, None)
            if enabled:
                env[key] = '1'
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

    def test_automatic_checks_union_each_branch_even_when_later_edits_revert_a_path(self):
        for name, crate in [('left', 'alpha'), ('right', 'beta')]:
            project = self.repo/'tests/projects'/name
            (project/'Reference').mkdir(parents=True)
            (project/'Reference/control.json').write_text('{}\n')
            (project/'gate.json').write_text(json.dumps({
                'commands': [name+'-check'], 'engine_paths': ['crates/'+crate+'/'],
                'fast_commands': ['shared-fast-check']}))
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): automatic train fixtures')
        first = self.branch('first', {'crates/alpha/src/lib.rs': '// edited\n'})
        second = self.branch('second', {'crates/alpha/src/lib.rs': '// base\n',
                                        'crates/beta/src/lib.rs': '// edited\n'}, base='first')
        with self.hold():
            workers = [self.start(first)]
            self.registered(1)
            workers.append(self.start(second))
            self.registered(2)
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 0], results)
        gates = self.gates()
        self.assertEqual(len(gates), 1)
        checks = gates[0]['automatic']
        self.assertEqual(sum(row[1] == 'shared-fast-check' for row in checks), 1, gates)
        suites = [row[0] for row in checks if 'tools/agents/suite.py' in row[1]]
        self.assertEqual(sorted(suites), ['left', 'right'])
        self.assertEqual(len({land.automatic_key(row[1], row[3]) for row in checks}), len(checks))

    def test_failure_isolation_keeps_automatic_checks_from_the_landed_prefix(self):
        project = self.repo/'tests/projects/left'
        (project/'Reference').mkdir(parents=True)
        (project/'Reference/control.json').write_text('{}\n')
        (project/'gate.json').write_text(json.dumps({
            'commands': ['left-check'], 'engine_paths': ['crates/alpha/']}))
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): automatic prefix fixture')
        first = self.branch('first', {'crates/alpha/src/lib.rs': '// edited\n'})
        second = self.branch('second', {'bad.txt': 'fail\n'})
        third = self.branch('third', {'break-left.txt': 'yes\n'})
        with self.hold():
            workers = []
            for count, path in enumerate((first, second, third), 1):
                workers.append(self.start(path))
                self.registered(count)
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 5, 5], results)
        self.assertEqual([row['branch'] for row in self.rows()], ['first'])
        self.assertTrue(any(row[0] == 'left' for row in self.gates()[-1]['automatic']))

    def test_invalid_automatic_metadata_rejects_only_its_branch(self):
        first = self.branch('first', {'tests/projects/control/gate.json':
                                     '{"commands":["true"],"fast_commands":false}\n'})
        second = self.branch('second', {'good.txt': 'ok\n'})
        rejected = self.start(first)
        self.assertEqual(rejected[0].wait(timeout=5), 2)
        with self.hold():
            peer = self.start(second)
            self.registered(1)
        results = self.finish([peer])
        self.assertEqual(results[0][0], 0, results)
        self.assertEqual([row['branch'] for row in self.rows()], ['second'])

    def test_train_preserves_each_callers_automatic_release_override(self):
        metadata = self.repo/'tests/projects/control/gate.json'
        metadata.write_text(json.dumps({'commands': ['true'], 'fast_commands': ['shared-fast']}))
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): release control metadata')
        first = self.branch('first', {'a.txt': 'ok\n'})
        second = self.branch('second', {'b.txt': 'ok\n'})
        with self.hold():
            workers = [self.start(first, probe_release='6000.0.71f1')]
            self.registered(1)
            workers.append(self.start(second, probe_release='2022.3.10f1'))
            self.registered(2)
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [0, 0], results)
        checks = self.gates()[0]['automatic']
        self.assertEqual({row[3] for row in checks}, {'6000.0.71f1', '2022.3.10f1'})
        self.assertEqual([row[1] for row in checks], ['shared-fast', 'shared-fast'])

    def test_rejected_leader_returns_before_a_peers_gate_completes(self):
        first = self.branch('first', {'shared.txt': 'branch edit\n'})
        second = self.branch('second', {'good.txt': 'ok\n'})
        (self.repo/'shared.txt').write_text('main edit\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): concurrent main control')
        (self.state/'pause-leader').write_text('pause')
        rejected = self.start(first, pause_leader=True)
        self.wait_for(lambda: (self.state/'leader-running').exists(), 'leader did not start')
        peer = self.start(second)
        self.registered(2)
        (self.state/'pause-gate').write_text('pause')
        (self.state/'pause-leader').unlink()
        self.assertEqual(rejected[0].wait(timeout=4), 3)
        self.wait_for(lambda: (self.state/'gate-running').exists(), 'peer did not take over')
        self.assertIsNone(peer[0].poll())
        self.assertEqual(self.rows(), [])
        (self.state/'pause-gate').unlink()
        self.assertEqual(self.finish([peer])[0][0], 0)
        self.assertEqual([row['branch'] for row in self.rows()], ['second'])

    def test_rejected_ancestor_automatic_checks_survive_a_leader_handoff(self):
        project = self.repo/'tests/projects/left'
        (project/'Reference').mkdir(parents=True)
        (project/'Reference/control.json').write_text('{}\n')
        (project/'gate.json').write_text(json.dumps({
            'commands': ['left-check'], 'engine_paths': ['crates/alpha/']}))
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): inherited automatic control')
        first = self.branch('first', {'a.txt': 'wrong\n', 'crates/alpha/src/lib.rs': '// edited\n'})
        second = self.branch('second', {'a.txt': 'ok\n', 'crates/alpha/src/lib.rs': '// base\n',
                                       'break-left.txt': 'yes\n'}, base='first')
        (self.state/'pause-leader').write_text('pause')
        owner = self.start(first, ['probe-a'], pause_leader=True)
        self.wait_for(lambda: (self.state/'leader-running').exists(), 'leader did not start')
        peer = self.start(second)
        self.registered(2)
        (self.state/'pause-leader').unlink()
        results = self.finish([owner, peer])
        self.assertEqual([code for code, _ in results], [5, 5], results)
        self.assertEqual(self.rows(), [])

    def test_queued_metadata_edits_do_not_enter_a_stacked_peers_checks(self):
        metadata = self.repo/'tests/projects/control/gate.json'
        metadata.write_text(json.dumps({'commands': ['true'], 'fast_commands': ['good-check']}))
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): queued metadata control')
        first = self.branch('first', {'a.txt': 'ok\n'})
        second = self.branch('second', {'b.txt': 'ok\n'}, base='first')
        (self.state/'pause-leader').write_text('pause')
        owner = self.start(first, pause_leader=True)
        self.wait_for(lambda: (self.state/'leader-running').exists(), 'leader did not start')
        peer = self.start(second)
        self.registered(2)
        edited = first/'tests/projects/control/gate.json'
        edited.write_text(json.dumps({'commands': ['true'], 'fast_commands': ['uncommitted-check']}))
        (self.state/'pause-leader').unlink()
        results = self.finish([owner, peer])
        self.assertEqual([code for code, _ in results], [2, 0], results)
        checks = self.gates()[-1]['automatic']
        self.assertEqual([row[1] for row in checks], ['good-check'])
        self.assertIn('uncommitted-check', edited.read_text())

    def test_legacy_queue_requests_select_checks_with_current_callers(self):
        metadata = self.repo/'tests/projects/control/gate.json'
        metadata.write_text(json.dumps({'commands': ['true'], 'fast_commands': ['legacy-fast']}))
        self.git('add', '.')
        self.git('commit', '-qm', 'test(agents): legacy queue control')
        first = self.branch('first', {'a.txt': 'ok\n'})
        second = self.branch('second', {'b.txt': 'ok\n'})
        with self.hold():
            owner = self.start(first, legacy=True)
            self.registered(1)
            peer = self.start(second, probe_release='6000.0.71f1')
            self.registered(2)
        results = self.finish([owner, peer])
        self.assertEqual([code for code, _ in results], [0, 0], results)
        self.assertEqual([row[1] for row in self.gates()[0]['automatic']], ['legacy-fast'])

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
        self.assertEqual(json.loads((self.state/'land-publications.json').read_text())['trains'], {})
        self.assertEqual(self.git('for-each-ref', '--format=%(refname)', 'refs/unbound/land-publications/'), '')
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

    def test_terminal_recovery_pushes_after_the_last_main_advance(self):
        first = self.branch('first', {'a.txt': 'ok'})
        second = self.branch('second', {'b.txt': 'ok'})
        pause = self.state/'pause-leader'
        pause.write_text('pause')
        workers = [self.start(first, pause_leader=True, crash_final=True)]
        self.wait_for(lambda: (self.state/'leader-running').exists(), 'leader did not acquire lock')
        workers.append(self.start(second))
        self.registered(2)
        pause.unlink()
        results = self.finish(workers)
        self.assertEqual([code for code, _ in results], [73, 0], results)
        self.assertEqual([row['branch'] for row in self.rows()], ['first', 'second'])
        self.assertEqual(sum('"push"' in line for line in self.events.read_text().splitlines()), 1)

    def ready_caller_recovers(self, final, after_record=False):
        first = self.branch('first', {'a.txt': 'ok'})
        second = self.branch('second', {'b.txt': 'ok'})
        pause = self.state/'pause-lock'
        pause.write_text('pause')
        workers = [self.start(first, pause_lock=True)]
        self.wait_for(lambda: (self.state/'before-lock').exists(), 'first caller did not register')
        workers.append(self.start(second, crash_final=final and not after_record, crash_prefix=not final,
                                  crash_after_final_record=after_record))
        self.assertEqual(workers[1][0].wait(timeout=20), 73, workers[1][1].read_text())
        pause.unlink()
        results = self.finish(workers)
        self.assertEqual(results[0][0], 0, results)
        self.assertEqual([row['branch'] for row in self.rows()], ['first', 'second'] if final else ['first'])
        self.assertEqual(sum('"push"' in line for line in self.events.read_text().splitlines()), 1)

    def test_ready_caller_recovers_another_advanced_branch(self):
        self.ready_caller_recovers(True)

    def test_publication_survives_completion_of_every_caller_result(self):
        self.ready_caller_recovers(True, after_record=True)

    def test_ready_caller_pushes_an_advanced_prefix(self):
        self.ready_caller_recovers(False)

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



class AutomaticProbeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.gate = land.Gate(self.root, 'HEAD', [], False, self.root/'gate.log')

    def probe(self, name, spec=None):
        path = self.root/'tests/projects'/name
        (path/'Reference').mkdir(parents=True)
        if spec is not None:
            (path/'gate.json').write_text(json.dumps(spec))

    def select(self, changed, explicit=()):
        return land.automatic_probes(self.root, changed, explicit)

    def test_changed_files_and_declared_engine_paths_select_full_recipes(self):
        self.probe('own', {'commands': ['own-command']})
        self.probe('dependent', {'commands': ['dependent-command'], 'engine_paths': ['managed/Animation/', 'crates/*/src/model*.rs']})
        self.probe('unrelated', {'commands': ['unrelated-command']})
        for changed, names in [
            (['tests/projects/own/Assets/Probe.cs'], ['own']),
            (['tests/projects/own/README.md'], ['own']),
            (['managed/Animation/Animator.cs'], ['dependent']),
            (['crates/assets/src/model.rs'], ['dependent']),
            (['managed/AnimationExtra/Animator.cs'], []),
            (['README.md'], []),
        ]:
            with self.subTest(changed=changed):
                selected = self.select(changed)
                self.assertEqual([row[0] for row in selected], names)
                for name, command, _ in selected:
                    argv = shlex.split(command)
                    self.assertEqual(argv[argv.index('--only') + 1], name)
                    self.assertNotIn('--against', argv)  # retain the suite's sheriff baseline policy

    def test_fast_checks_run_without_an_engine_path_match_and_are_deduplicated(self):
        self.probe('short', {'commands': ['long-run'], 'fast_commands': ['doctor-check', 'doctor-check']})
        selected = self.select(['crates/core/src/lib.rs'])
        self.assertEqual(selected, [('short', 'doctor-check', 'fast check')])
        self.assertEqual(self.select(['crates/core/src/lib.rs'], ['doctor-check']), [])
        selected = self.select(['tests/projects/short/run.py'])
        self.assertEqual(len(selected), 2)
        self.assertIn('tools/agents/suite.py', selected[0][1])
        self.assertEqual(selected[1][1], 'doctor-check')

    def test_explicit_complete_declared_recipe_is_not_repeated(self):
        self.probe('custom', {'commands': ['prepare', 'compare']})
        self.assertEqual(self.select(['tests/projects/custom/run.py'], ['prepare', 'compare']), [])
        self.assertEqual(len(self.select(['tests/projects/custom/run.py'], ['prepare'])), 1)

    def test_suite_only_probe_and_explicit_suite_command_are_supported(self):
        self.probe('humanfirstpose', {'engine_paths': ['tests/projects/humanoid/']})
        paths = ['tests/projects/humanoid/Assets/Scripts/Probe.cs']
        self.assertEqual([row[0] for row in self.select(paths)], ['humanfirstpose'])
        explicit = ['python3 tools/agents/suite.py --only humanfirstpose --against none']
        self.assertEqual(self.select(paths, explicit), [])
        echoed = ['echo python3 tools/agents/suite.py --only humanfirstpose']
        self.assertEqual([row[0] for row in self.select(paths, echoed)], ['humanfirstpose'])
        commented = ['python3 -c \"print(1)\" tools/agents/suite.py --only humanfirstpose']
        self.assertEqual([row[0] for row in self.select(paths, commented)], ['humanfirstpose'])

    def test_suite_coverage_requires_execution_final_selection_and_matching_release(self):
        self.probe('humanfirstpose', {'engine_paths': ['managed/Animation/']})
        paths = ['managed/Animation/Animator.cs']
        prefix = 'python3 tools/agents/suite.py --only humanfirstpose'
        for suffix in ('--list', '--help', '--promote existing', '--release 2022.3.10f1',
                       '--only other', '--only=other'):
            with self.subTest(suffix=suffix):
                self.assertEqual([row[0] for row in self.select(paths, [prefix+' '+suffix])], ['humanfirstpose'])
        self.assertEqual(self.select(paths, [prefix+' --release 6000.0.71f1']), [])
        check = ('humanfirstpose', prefix, 'engine paths', '6000.0.71f1')
        for command in (prefix+' --list', prefix+' --release 2022.3.10f1'):
            self.assertEqual(land.automatic_union([[check]], [(command, '6000.0.71f1')]), [check])
        self.assertEqual(land.automatic_union([[check]], [(prefix+' --release 6000.0.71f1', '2022.3.10f1')]), [])

    def test_recipe_validation_reads_the_candidate_suite_and_bounds_imports(self):
        self.probe('candidate')
        path = self.root/'tools/agents/suite.py'
        path.parent.mkdir(parents=True)
        (path.parent/'candidate_helper.py').write_text('marker = "candidate"\n')
        body = ('from dataclasses import dataclass\nimport candidate_helper\n'
                'print("candidate import diagnostic")\n@dataclass\nclass Step:\n    value: int = 1\n'
                'class NoRecipe:\n    def __init__(self, reason): self.reason = reason\n'
                'def recipes(release): return {candidate_helper.marker: [Step()]}\n'
                'def recipe_for(name, table, release, projects=None):\n'
                '    return table.get(name, NoRecipe("candidate missing recipe"))\n')
        path.write_text(body)
        paths = ['tests/projects/candidate/run.py']
        with mock.patch.dict(sys.modules, {'candidate_helper': mock.Mock(marker='leader-wrong')}):
            self.assertEqual([row[0] for row in self.select(paths)], ['candidate'])
        path.write_text(body.replace('{candidate_helper.marker: [Step()]}', '{}'))
        with self.assertRaises(SystemExit):
            self.select(paths)
        path.write_text('import time; time.sleep(30)\n')
        started = time.monotonic()
        with mock.patch.object(land, 'AUTO_SELECTION_TIMEOUT_SECONDS', .1), self.assertRaises(SystemExit):
            self.select(paths)
        self.assertLess(time.monotonic()-started, 5)

    def test_candidate_catalog_imports_each_release_without_claiming_priority(self):
        path = self.root/'tools/agents/suite.py'
        path.parent.mkdir(parents=True)
        path.write_text('import os\n'
                        'assert os.environ.get("UNBOUND_SUITE_NO_EDITOR") == "1"\n'
                        'assert "UNBOUND_LAND_GATE" not in os.environ\n'
                        'captured = os.environ["UNBOUND_PROBE_UNITY_VERSION"]\n'
                        'class NoRecipe: pass\n'
                        'def recipes(release):\n'
                        '    assert release == captured\n'
                        '    return {"probe-"+captured: ["step"]}\n'
                        'def recipe_for(name, table, release, projects=None): return table[name]\n')
        requested = [('probe-6000.0.71f1', '6000.0.71f1'), ('probe-2022.3.10f1', '2022.3.10f1')]
        with mock.patch.dict(os.environ, {'UNBOUND_LAND_GATE': '1'}):
            catalog = land.candidate_recipes(self.root, requested)
        self.assertEqual(set(catalog), {name for name, _ in requested})
        self.assertTrue(all(row['builtin'] and row['error'] is None for row in catalog.values()))

    def test_invalid_metadata_fails_instead_of_omitting_checks(self):
        for spec in [{'commands': ['true'], 'engine_paths': 'crates/'},
                     {'commands': ['true'], 'engine_paths': ['/crates/']},
                     {'commands': ['true'], 'engine_paths': ['../crates/']},
                     {'commands': ['true'], 'fast_commands': [False]},
                     {'commands': ['true'], 'fast_command': ['true']}]:
            with self.subTest(spec=spec):
                path = self.root/'tests/projects/bad'
                (path/'Reference').mkdir(parents=True, exist_ok=True)
                (path/'gate.json').write_text(json.dumps(spec))
                with self.assertRaises(SystemExit) as error:
                    self.select(['crates/core/src/lib.rs'])
                self.assertEqual(error.exception.code, 2)

    def test_support_projects_without_references_do_not_add_automatic_recipes(self):
        for name in ('packages', 'query2dskinmerge'):
            project = self.root/'tests/projects'/name
            project.mkdir(parents=True)
            for filename in ('README.md', 'validate.py'):
                changed = [f'tests/projects/{name}/{filename}']
                self.assertEqual(self.select(changed), [])
                self.assertEqual(self.select(changed, [f'python3 tests/projects/{name}/validate.py']), [])
        self.probe('actual', {'commands': ['true'], 'engine_paths': ['tests/projects/packages/']})
        self.assertEqual([row[0] for row in self.select(['tests/projects/packages/Assets/Probe.cs'])], ['actual'])

    def test_a_touched_probe_without_a_recipe_is_an_error(self):
        self.probe('missingrecipe')
        with self.assertRaises(SystemExit) as error:
            self.select(['tests/projects/missingrecipe/run.py'])
        self.assertEqual(error.exception.code, 2)

    def test_a_failed_automatic_check_fails_the_gate(self):
        self.gate.automatic = [('a', 'exit 1', 'fast check')]
        self.gate.log.write_text('failing control\n')
        with mock.patch.object(self.gate, 'step', return_value=1):
            with self.assertRaises(SystemExit) as error:
                self.gate.run_automatic_probes()
        self.assertEqual(error.exception.code, 5)

    def test_automatic_checks_are_guarded_released_and_budgeted(self):
        self.gate.automatic = [('a', 'echo check', 'fast check', '2022.3.10f1')]
        with mock.patch.object(self.gate, 'step', return_value=0) as step, contextlib.redirect_stdout(io.StringIO()) as out:
            self.gate.run_automatic_probes()
        self.assertIn(f'budget {land.AUTO_PROBE_BUDGET_SECONDS}s total', out.getvalue())
        kwargs = step.call_args.kwargs
        self.assertTrue(kwargs['guard_editor'])
        self.assertEqual(kwargs['env'][land.NO_EDITOR], '1')
        self.assertEqual(kwargs['env'][land.RELEASE_VARIABLE], '2022.3.10f1')
        self.assertGreater(kwargs['timeout'], 0)
        self.assertLessEqual(kwargs['timeout'], land.AUTO_PROBE_TIMEOUT_SECONDS)

        self.gate.automatic = [('a', 'echo check', 'fast check')]
        with mock.patch.object(self.gate, 'step', return_value=0) as step:
            self.gate.run_automatic_probes()
        self.assertEqual(step.call_args.kwargs['env'][land.RELEASE_VARIABLE], land.default_release())

    def test_total_budget_exhaustion_fails_before_starting_another_check(self):
        self.gate.automatic = [('a', 'first', 'fast check'), ('b', 'second', 'fast check')]
        with mock.patch.object(self.gate, 'step', return_value=0) as step, mock.patch.object(land.time, 'monotonic', side_effect=[0, 0, land.AUTO_PROBE_BUDGET_SECONDS+1]):
            with self.assertRaises(SystemExit) as error:
                self.gate.run_automatic_probes()
        self.assertEqual(error.exception.code, 5)
        self.assertEqual(step.call_count, 1)

    def test_timeout_stops_the_process_tree_and_reports_failure(self):
        command = shlex.join([sys.executable, '-c', 'import time; time.sleep(30)'])
        started = time.monotonic()
        code = self.gate.step('bounded control', command, shell=True, guard_editor=True, timeout=0.1)
        self.assertEqual(code, 124)
        self.assertLess(time.monotonic() - started, 5)
        self.assertIn('exceeded its', self.gate.log.read_text())

    def test_docs_only_gate_retains_its_build_and_test_exemption(self):
        with mock.patch.object(land, 'git', return_value='docs/ARCHITECTURE.md'), mock.patch.object(land.subprocess, 'run') as run, mock.patch.object(self.gate, 'step') as step:
            run.return_value.returncode = 0
            self.gate.run()
        step.assert_not_called()
        self.assertEqual(run.call_count, 1)  # oracle guard still runs

    def test_repeated_automatic_suite_execution_uses_fresh_output_labels(self):
        self.gate.automatic = [('sample', 'python3 tools/agents/suite.py --only sample --label recorded',
                                'changed files', '6000.0.71f1')]
        with mock.patch.object(self.gate, 'step', return_value=0) as step:
            self.gate.run_automatic_probes()
            self.gate.run_automatic_probes()
        labels = []
        for call in step.call_args_list:
            argv = shlex.split(call.args[1])
            labels.append(argv[argv.index('--label')+1])
            self.assertEqual(argv[argv.index('--only')+1], 'sample')
        self.assertNotIn('recorded', labels)
        self.assertEqual(len(set(labels)), 2)

    def test_train_union_deduplicates_generated_labels_and_keeps_releases(self):
        def check(label, release):
            return ('sample', 'python3 tools/agents/suite.py --only sample --jobs 1 --label '+label,
                    'changed files', release)
        a, b = check('first', '6000.0.71f1'), check('second', '6000.0.71f1')
        b = (b[0], b[1].replace('python3', '/usr/bin/python3', 1), b[2], b[3])
        c = check('third', '2022.3.10f1')
        self.assertEqual(land.automatic_union([[a], [b, c]]), [a, c])
        self.assertEqual(land.automatic_union([[a, b]], [(a[1], a[3])]), [])
        self.assertEqual(land.automatic_union([[a, b]],
            [('python3 tools/agents/suite.py --only sample --against none', a[3])]), [])
        self.assertEqual(land.stamp_path('tree', [], False, self.root, automatic=[a]),
                         land.stamp_path('tree', [], False, self.root, automatic=[b]))
        self.assertNotEqual(land.stamp_path('tree', [], False, self.root, automatic=[a]),
                            land.stamp_path('tree', [], False, self.root, automatic=[c]))

    def test_automatic_suites_freeze_the_baseline_and_missing_baselines_still_fail(self):
        import suite
        state = self.root/'state'
        source = state/'baseline/summary.json'
        source.parent.mkdir(parents=True)
        source.write_text('{"probes":{"sample":{"status":"fail","failures":2}}}')
        row = ('sample', 'python3 tools/agents/suite.py --only sample --label selected',
               'changed files', '6000.0.71f1')
        with mock.patch.object(land, 'STATE', state):
            first = land.freeze_automatic_baseline([row])[0]
            argv = shlex.split(first[1])
            saved = Path(argv[argv.index('--against')+1])
            source.write_text('{"probes":{}}')
            second = land.freeze_automatic_baseline([row])[0]
            self.assertNotEqual(first[1], second[1])
            self.assertEqual(json.loads(saved.read_text())['probes']['sample']['failures'], 2)
            source.unlink()
            empty = land.freeze_automatic_baseline([row])[0]
            argv = shlex.split(empty[1])
            previous = json.loads(Path(argv[argv.index('--against')+1]).read_text())
            current = {'probes': {'sample': {'status': 'fail', 'failures': 1}}}
            self.assertTrue(suite.compare_runs(current, previous)[1])

    def test_full_keeps_static_checks_active_for_docs_only_changes(self):
        self.probe('short', {'commands': ['long-run'], 'fast_commands': ['doctor-check']})
        paths = ['docs/ARCHITECTURE.md']
        self.assertEqual(land.automatic_checks(self.root, paths, []), [])
        selected = land.automatic_checks(self.root, paths, [], full=True)
        self.assertEqual([(row[0], row[1]) for row in selected], [('short', 'doctor-check')])

    def test_gate_stamps_include_the_current_sheriff_baseline(self):
        state = self.root/'state'
        (state/'baseline').mkdir(parents=True)
        baseline = state/'baseline/summary.json'
        with mock.patch.object(land, 'STATE', state):
            before = land.stamp_path('tree', [], False, self.root, base='base')
            baseline.write_text('{"probes": {}}')
            first = land.stamp_path('tree', [], False, self.root, base='base')
            self.assertNotEqual(before, first)
            baseline.write_text('{"probes": {"a": {}}}')
            self.assertNotEqual(first, land.stamp_path('tree', [], False, self.root, base='base'))

    def test_gate_stamps_include_the_changed_range_and_automatic_release(self):
        a = land.stamp_path('tree', [], False, self.root, base='base-a')
        self.assertNotEqual(a, land.stamp_path('tree', [], False, self.root, base='base-b'))
        with mock.patch.dict(os.environ, {land.RELEASE_VARIABLE: '2022.3.10f1'}):
            self.assertNotEqual(a, land.stamp_path('tree', [], False, self.root, base='base-a'))


if __name__ == '__main__':
    unittest.main()
