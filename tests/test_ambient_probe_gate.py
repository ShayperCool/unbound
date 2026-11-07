"""Negative paths must fail and retain full ambient capture diagnostics."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

PROBE = Path(__file__).resolve().parent/'projects/ambient'


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, PROBE/filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


runner = load('ambient_runner_test', 'run.py')
verifier = load('ambient_verifier_test', 'verify.py')


class AmbientGateFailureTests(unittest.TestCase):
    def setUp(self):
        cache = Path.home()/'.cache/unbound'
        cache.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix='ambient-negative-', dir=cache)
        self.addCleanup(self.temporary.cleanup)
        self.work = Path(self.temporary.name)

    def verify(self):
        report = self.work/'report.json'
        with mock.patch.object(sys, 'argv', ['verify.py', '--release', '6000.0.71f1',
                                            '--actual', str(self.work), '--report', str(report)]), \
             contextlib.redirect_stdout(io.StringIO()):
            status = verifier.main()
        self.assertTrue(status)
        return json.loads(report.read_text())

    def test_missing_files_write_both_groups_and_every_missing_record(self):
        report = self.verify()
        for group, count in [('ambient', 7), ('sun', 6)]:
            failures = [f for f in report['failures'] if f['group'] == group]
            self.assertTrue(any('file' in f and 'error' in f for f in failures))
            self.assertEqual(count, sum('key' in f for f in failures))

    def test_empty_array_and_malformed_lines_preserve_all_other_differences(self):
        records = [json.loads(line) for line in (PROBE/'Reference/ambient.jsonl').read_text().splitlines()]
        records[0]['probe'] = []
        records[1]['probe'][0] += 1
        (self.work/'ambient').mkdir()
        (self.work/'ambient/ambient.jsonl').write_text('\n'.join(map(json.dumps, records))+'\n{broken\n[]\n')
        failures = self.verify()['failures']
        initial = [f for f in failures if f.get('key') == 'initial']
        self.assertEqual(list(range(27)), [f['coefficient'] for f in initial])
        self.assertTrue(any(f.get('key') == 'default' and f.get('coefficient') == 0 for f in failures))
        self.assertTrue(any(f.get('raw') == '{broken' for f in failures))
        self.assertTrue(any(f.get('raw') == [] for f in failures))

    def test_failed_editor_record_returns_failure(self):
        result = {'exit_code': 1}
        with mock.patch.object(sys, 'argv', ['run.py', 'record', '--release', '6000.0.71f1', '--work', str(self.work)]), \
             mock.patch.object(runner, 'copy_project', return_value=self.work/'project'), \
             mock.patch.object(runner, 'run', return_value=result) as launch, \
             mock.patch.object(runner, 'result_problem', return_value=None), \
             mock.patch.object(runner, 'complete', return_value=None), \
             contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(1, runner.main())
        self.assertEqual(1, launch.call_count)

    def test_successful_editor_without_complete_capture_returns_failure(self):
        with mock.patch.object(sys, 'argv', ['run.py', 'record', '--release', '6000.0.71f1', '--work', str(self.work)]), \
             mock.patch.object(runner, 'copy_project', return_value=self.work/'project'), \
             mock.patch.object(runner, 'run', return_value={'exit_code': 0}), \
             mock.patch.object(runner, 'result_problem', return_value=None), \
             contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(1, runner.main())

    def test_legacy_comparator_failure_cannot_prevent_report(self):
        calls = []
        def command(args, **kwargs):
            calls.append(args)
            if str(PROBE/'verify.py') in args:
                (self.work/'report.json').write_text('{"failures": ["missing capture"]}')
            return mock.Mock(returncode=1)
        with mock.patch.object(sys, 'argv', ['run.py', 'gate', '--release', '6000.0.71f1', '--work', str(self.work)]), \
             mock.patch.dict('os.environ', {'UNBOUND': '/unused/unbound'}), \
             mock.patch.object(runner, 'copy_project', return_value=self.work/'project'), \
             mock.patch.object(runner.subprocess, 'run', side_effect=command):
            self.assertEqual(1, runner.main())
        self.assertTrue((self.work/'report.json').is_file())
        scripts = [args[1] for args in calls[2:]]
        self.assertEqual(str(PROBE/'verify.py'), scripts[0])
        self.assertIn(str(PROBE/'compare.py'), scripts)
        self.assertIn(str(PROBE.parent/'particles/compare_images.py'), scripts)


if __name__ == '__main__':
    unittest.main()
