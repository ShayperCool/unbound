"""Isolated controls for plan transport; no engine, SDK, build or installed lease."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

import plan_runner as runner


class PlanRunnerTests(unittest.TestCase):
    def setUp(self):
        parent = Path(os.environ.get('UNBOUND_PLAN_RUNNER_TEST_OUT',
                      Path.home()/'.cache/unbound/validation/runs/plan-runner-controls-20261105'))
        parent.mkdir(parents=True, exist_ok=True)
        self.root = Path(tempfile.mkdtemp(prefix=self._testMethodName+'-', dir=parent)).resolve()
        self.python = str(Path(sys.executable).resolve())
        self.launcher = self.root/'launcher'
        self.launcher.write_text('#!'+self.python+'\nimport os,sys\n'
                                 'os.execv(sys.argv[5],sys.argv[5:])\n')
        self.launcher.chmod(0o700)
        self.systemctl = self.root/'systemctl'
        self.systemctl.write_text('#!'+self.python+'\nimport sys\nraise SystemExit(0)\n')
        self.systemctl.chmod(0o700)
        self.input = self.root/'input.txt'
        self.input.write_text('frozen')
        self.plan_path, self.approval_path = self.root/'plan.json', self.root/'approval.json'
        self.run = self.root/'run'
        self.plan = dict(version=1, run_dir=str(self.run), launcher=str(self.launcher),
                         timeout=str(Path('/usr/bin/timeout').resolve()), systemctl=str(self.systemctl),
                         env={'PATH': '/usr/bin:/bin', 'HOME': str(self.root)},
                         inputs={}, jobs=[self.job('first', 'print("payload")')])
        self.pin()

    def job(self, name, code, timeout=3):
        return dict(id=name, kind='tool', argv=[self.python, '-c', code],
                    cwd='{run}', timeout_s=timeout)

    def pin(self):
        paths = [self.launcher, self.systemctl, Path('/usr/bin/timeout').resolve(),
                 Path(self.python), self.input]
        self.plan['inputs'] = {str(p): runner.digest(p) for p in paths}

    def write(self):
        self.plan_path.write_text(json.dumps(self.plan))
        self.approval = dict(version=1, issued_by='Root', plan_sha256=runner.digest(self.plan_path),
                             runner_sha256=runner.digest(runner.__file__), run_dir=str(self.run),
                             job_ids=[j['id'] for j in self.plan['jobs']])
        self.approval_path.write_text(json.dumps(self.approval))

    def execute(self):
        return runner.execute(self.plan_path, self.approval_path)

    def test_check_is_read_only(self):
        self.write()
        runner.check(self.plan_path, self.approval_path)
        self.assertFalse(self.run.exists())

    def test_interrupted_receipt_write_can_be_finalized(self):
        self.run.mkdir()
        output = self.run/'result.json'
        runner.save(output, {'state': 'running'})
        with patch.object(runner.json, 'dump', side_effect=KeyboardInterrupt):
            with self.assertRaises(KeyboardInterrupt):
                runner.save(output, {'state': 'running', 'attempts': ['first']})
        self.assertEqual(json.loads(output.read_text()), {'state': 'running'})
        runner.save(output, {'state': 'stopped'})
        self.assertEqual(json.loads(output.read_text()), {'state': 'stopped'})
        self.assertEqual(list(self.run.glob('*.tmp')), [])

    def test_complete_real_processes_raw_results_and_private_modes(self):
        self.plan['jobs'].append(self.job('second', 'print("second")'))
        self.write()
        old = os.umask(0o077)
        try:
            result = self.execute()
        finally:
            os.umask(old)
        self.assertEqual(result['state'], 'complete')
        self.assertEqual(result['completed'], ['first', 'second'])
        self.assertEqual([r['raw_exit'] for r in result['attempts']], [0, 0])
        self.assertFalse(result['admission'])
        self.assertEqual(self.run.stat().st_mode & 0o777, 0o700)
        self.assertEqual((self.run/'first.log').stat().st_mode & 0o777, 0o600)
        self.assertEqual((self.run/'result.json').stat().st_mode & 0o777, 0o600)
        self.assertNotEqual(result['attempts'][0]['scope'], result['attempts'][1]['scope'])

    def test_cannot_resume_or_repeat_completed_run(self):
        self.write()
        self.execute()
        before = (self.run/'result.json').read_bytes()
        with self.assertRaises(runner.Refused):
            self.execute()
        self.assertEqual(before, (self.run/'result.json').read_bytes())

    def test_failed_command_is_charged_and_next_does_not_run(self):
        self.plan['jobs'] = [self.job('bad', 'raise SystemExit(7)'),
                             self.job('never', 'print("unexpected")')]
        self.write()
        with self.assertRaises(runner.Refused):
            self.execute()
        result = json.loads((self.run/'result.json').read_text())
        self.assertEqual(result['state'], 'stopped')
        self.assertEqual(len(result['attempts']), 1)
        self.assertEqual(result['attempts'][0]['raw_exit'], 7)
        self.assertEqual(result['completed'], [])
        self.assertFalse((self.run/'never.log').exists())

    def test_input_mutation_during_command_stops_before_next(self):
        self.plan['jobs'] = [self.job('mutate',
            f'from pathlib import Path;Path({str(self.input)!r}).write_text("changed")'),
            self.job('never', 'pass')]
        self.write()
        with self.assertRaisesRegex(runner.Refused, 'input changed'):
            self.execute()
        result = json.loads((self.run/'result.json').read_text())
        self.assertEqual(len(result['attempts']), 1)
        self.assertEqual(result['attempts'][0]['raw_exit'], 0)
        self.assertEqual(result['completed'], [])

    def test_timeout_stops_payload_and_no_next_step(self):
        self.plan['jobs'] = [self.job('slow', 'import time;time.sleep(30)', .1),
                             self.job('never', 'pass')]
        self.write()
        start = time.monotonic()
        with self.assertRaises(runner.Refused):
            self.execute()
        self.assertLess(time.monotonic() - start, 10)
        result = json.loads((self.run/'result.json').read_text())
        self.assertEqual(result['attempts'][0]['raw_exit'], 124)
        self.assertFalse((self.run/'never.log').exists())

    def test_parent_environment_is_not_inherited_or_recorded(self):
        self.plan['jobs'] = [self.job('env',
            'import os;assert "PLAN_RUNNER_SECRET" not in os.environ;print("clean")')]
        self.write()
        old = os.environ.get('PLAN_RUNNER_SECRET')
        os.environ['PLAN_RUNNER_SECRET'] = 'never-copy-this-value'
        try:
            result = self.execute()
        finally:
            if old is None:
                del os.environ['PLAN_RUNNER_SECRET']
            else:
                os.environ['PLAN_RUNNER_SECRET'] = old
        self.assertEqual(result['state'], 'complete')
        self.assertNotIn('never-copy-this-value', (self.run/'result.json').read_text())

    def test_missing_input_refuses_before_output_claim(self):
        self.write()
        self.input.unlink()
        with self.assertRaises(runner.Refused):
            self.execute()
        self.assertFalse(self.run.exists())

    def test_stock_launcher_home_dependency_is_checked_before_cost(self):
        stock = self.root/'with-heavy-job'
        stock.write_bytes(self.launcher.read_bytes())
        stock.chmod(0o700)
        self.plan['launcher'] = str(stock)
        self.plan['inputs'][str(stock)] = runner.digest(stock)
        self.write()
        with self.assertRaisesRegex(runner.Refused, 'launcher dependency'):
            self.execute()
        self.assertFalse(self.run.exists())

    def test_stock_launcher_explicit_dependencies_pass_readiness(self):
        stock = self.root/'with-heavy-job'
        stock.write_bytes(self.launcher.read_bytes())
        stock.chmod(0o700)
        self.plan['launcher'] = str(stock)
        self.plan['inputs'][str(stock)] = runner.digest(stock)
        cache = self.root/'.cache/compat'
        cache.mkdir(parents=True)
        for name in ['job_runner.py', 'job-limits.json']:
            dependency = cache/name
            dependency.write_text('pinned test stand-in')
            self.plan['inputs'][str(dependency)] = runner.digest(dependency)
        self.write()
        runner.check(self.plan_path, self.approval_path)
        self.assertFalse(self.run.exists())

    def test_drifted_input_refuses_before_output_claim(self):
        self.write()
        self.input.write_text('changed')
        with self.assertRaisesRegex(runner.Refused, 'input hash'):
            self.execute()
        self.assertFalse(self.run.exists())

    def test_plan_drift_refuses_before_output_claim(self):
        self.write()
        self.plan_path.write_text(json.dumps(dict(self.plan, version=2)))
        with self.assertRaises(runner.Refused):
            self.execute()
        self.assertFalse(self.run.exists())

    def test_parsed_plan_and_hash_use_the_same_byte_snapshot(self):
        self.write()
        original_read = Path.read_bytes
        original_plan = self.plan_path.read_bytes()
        changed = dict(self.plan, env={'PLAN_MARKER': 'changed'})
        changed_bytes = json.dumps(changed).encode()
        self.approval['plan_sha256'] = __import__('hashlib').sha256(changed_bytes).hexdigest()
        self.approval_path.write_text(json.dumps(self.approval))

        def read_then_replace(path):
            data = original_read(path)
            if path == self.plan_path:
                path.write_bytes(changed_bytes)
            return data

        with patch.object(Path, 'read_bytes', read_then_replace):
            with self.assertRaisesRegex(runner.Refused, 'plan hash'):
                self.execute()
        self.assertFalse(self.run.exists())
        self.assertNotEqual(original_plan, self.plan_path.read_bytes())

    def test_runner_mismatch_refuses(self):
        self.write()
        self.approval['runner_sha256'] = '0'*64
        self.approval_path.write_text(json.dumps(self.approval))
        with self.assertRaisesRegex(runner.Refused, 'runner hash'):
            self.execute()

    def test_job_sequence_cannot_expand_approval(self):
        self.write()
        self.approval['job_ids'] = []
        self.approval_path.write_text(json.dumps(self.approval))
        with self.assertRaisesRegex(runner.Refused, 'sequence'):
            self.execute()

    def test_invalid_plan_shapes_refused_before_output(self):
        for field, value in [('version', True), ('extra', True), ('jobs', []), ('env', [])]:
            with self.subTest(field=field):
                original = self.plan.copy()
                self.plan[field] = value
                self.write()
                with self.assertRaises(runner.Refused):
                    self.execute()
                self.plan = original
                self.assertFalse(self.run.exists())

    def test_invalid_timeouts_refused(self):
        for value in [0, -1, True, float('nan'), float('inf'), '3']:
            with self.subTest(value=value):
                self.plan['jobs'][0]['timeout_s'] = value
                self.write()
                with self.assertRaises(runner.Refused):
                    self.execute()

    def test_duplicate_json_key_refused(self):
        self.write()
        self.plan_path.write_text('{"version":1,"version":1}')
        with self.assertRaisesRegex(runner.Refused, 'duplicate'):
            self.execute()

    def test_output_symlink_refused(self):
        self.write()
        self.run.symlink_to(self.root/'elsewhere', target_is_directory=True)
        with self.assertRaises(runner.Refused):
            self.execute()

    def test_input_symlink_replacement_refused(self):
        self.write()
        copy = self.root/'same-bytes'
        copy.write_bytes(self.input.read_bytes())
        self.input.unlink()
        self.input.symlink_to(copy)
        with self.assertRaises(runner.Refused):
            self.execute()

    def test_signal_stops_own_payload_and_records_consumed_attempt(self):
        pidfile = self.root/'payload.pid'
        self.plan['jobs'] = [self.job('slow', 'import os,time;from pathlib import Path;'
            f'Path({str(pidfile)!r}).write_text(str(os.getpid()));time.sleep(30)')]
        self.write()
        with (self.root/'driver.log').open('wb') as log:
            proc = subprocess.Popen([self.python, str(Path(runner.__file__).resolve()), 'run',
                '--plan', str(self.plan_path), '--approval', str(self.approval_path)],
                stdout=log, stderr=subprocess.STDOUT)
            try:
                # Bounded synchronization of this test's child, not agent polling.
                deadline = time.monotonic() + 5
                while not pidfile.exists() and time.monotonic() < deadline:
                    time.sleep(.02)
                self.assertTrue(pidfile.exists())
                pid = int(pidfile.read_text())
                proc.send_signal(signal.SIGTERM)
                self.assertNotEqual(proc.wait(10), 0)
                result = json.loads((self.run/'result.json').read_text())
                self.assertEqual(result['state'], 'stopped')
                self.assertEqual(len(result['attempts']), 1)
                stat = Path('/proc', str(pid), 'stat')
                if stat.exists():
                    self.assertIn(stat.read_text().rsplit(')', 1)[1].split()[0], ['Z', 'X'])
            finally:
                if proc.poll() is None:
                    proc.kill()
                    proc.wait(5)


if __name__ == '__main__':
    unittest.main()
