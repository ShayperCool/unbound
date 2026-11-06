#!/usr/bin/env python3
"""Unit checks of the suite's regression rules: python3 -m unittest tools/agents/test_suite.py"""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('suite', Path(__file__).resolve().parent/'suite.py')
suite = importlib.util.module_from_spec(spec)
spec.loader.exec_module(suite)


def failing(summary, passed=0, failed=1, step=None, failures=None):
    result = {'status': 'fail', 'summary': summary, 'checks': {'passed': passed, 'failed': failed},
              'failed_step': step}
    if failures is not None:
        result['failures'] = failures
    return result


class WorseTests(unittest.TestCase):
    def test_more_differences(self):
        old = failing('17 nodes compared, 0 differences || 11 nodes compared, 1 differences')
        new = failing('17 nodes compared, 0 differences || 11 nodes compared, 7 differences')
        self.assertIsNotNone(suite.worse(old, new))
        self.assertIsNone(suite.worse(new, old))
        self.assertIsNone(suite.worse(old, old))

    def test_compact_json_counts(self):
        old = failing('{"tested":10,"failed":2061,"exact":false}')
        new = failing('{"tested":10,"failed":9000,"exact":false}')
        self.assertIsNotNone(suite.worse(old, new))

    def test_recorded_failures_beat_truncated_text(self):
        old = failing('cut', failures=3)
        new = failing('cut', failures=4)
        self.assertIsNotNone(suite.worse(old, new))
        self.assertIsNone(suite.worse(new, old))

    def test_earlier_failing_step(self):
        old = failing('x 1 differ || y 1 differ', failed=2)
        new = failing('x 1 differ || step 4 (driver) exited 1: boom', failed=1, step=4)
        self.assertIn('step 4', suite.worse(old, new))
        # Older summaries without failed_step: the step is read from the text, anywhere in it.
        del old['failed_step'], new['failed_step']
        self.assertIn('step 4', suite.worse(old, new))
        self.assertIn('step 3', suite.worse(failing('step 5 (driver) exited 1', step=5),
                                            failing('step 3 (engine) exited 1', step=3)))

    def test_getting_further_is_not_worse(self):
        crashed = failing('step 2 (driver) exited 1: boom', failed=0, step=2)
        compared = failing('5 missing', failed=1)
        self.assertIsNone(suite.worse(crashed, compared))
        later = failing('a 1 differ || step 6 (driver) exited 1', passed=1, failed=2, step=6)
        self.assertIsNone(suite.worse(failing('step 4 (driver) exited 1', passed=1, failed=0, step=4), later))

    def test_suite_error_is_worse_than_a_step_failure(self):
        error = {'probe': 'p', 'status': 'fail', 'failed_step': 0, 'seconds': 0, 'summary': 'suite error: boom'}
        self.assertIsNotNone(suite.worse(failing('step 2 (driver) exited 1', step=2), error))

    def test_compare_runs_flags_worse(self):
        old = failing('1 differences')
        new = failing('7 differences')
        lines, regressed = suite.compare_runs({'probes': {'p': new}}, {'probes': {'p': old}})
        self.assertTrue(regressed)
        self.assertTrue(lines[0].startswith('worse'))
        lines, regressed = suite.compare_runs({'probes': {'p': old}}, {'probes': {'p': new}})
        self.assertFalse(regressed)


if __name__ == '__main__':
    unittest.main()
