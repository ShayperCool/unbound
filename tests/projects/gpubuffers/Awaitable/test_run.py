"""Offline checks of comparison and publication rejection, using synthetic records."""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest

import run


class ContractComparisonTests(unittest.TestCase):
    def test_union_retains_every_missing_extra_and_failing_value(self):
        report = run.differences({'missing': [1, 2], 'bad': {'a': 3}},
                                 {'extra': [4, 5], 'bad': {'a': 6}},
                                 {'absent': [['c1:main:d-']]}, {'extra': ['a2:main:d-']})
        self.assertEqual(report, {
            'missing': [{'key': 'missing', 'reference': [1, 2]}],
            'extra': [{'key': 'extra', 'actual': [4, 5]}],
            'failing': [{'key': 'bad', 'reference': {'a': 3}, 'actual': {'a': 6}}],
            'timing': [{'key': 'absent', 'reference': [['c1:main:d-']], 'actual': None},
                       {'key': 'extra', 'reference': None, 'actual': ['a2:main:d-']}],
        })

    def test_one_and_two_frames_pass_three_and_zero_fail(self):
        reference = {'k': [['r0:returned', 'c1:main:d-', 'a1:main:d-']]}
        for frame in (0, 1, 2, 3):
            actual = {'k': ['r0:returned', f'c{frame}:main:d-', f'a{frame}:main:d-']}
            self.assertEqual(bool(run.differences({}, {}, reference, actual)['timing']), frame in (0, 3))

    def test_immediate_failure_cannot_move_to_next_frame(self):
        ref = {'k': [['c0:main:de', 'r0:returned', 'a0:main:de']]}
        self.assertFalse(any(run.differences({}, {}, ref, {'k': ref['k'][0]}).values()))
        self.assertTrue(run.differences({}, {}, ref, {'k': ['c1:main:de', 'r0:returned', 'a1:main:de']})['timing'])

    def test_order_thread_and_failure_outcome_remain_observable(self):
        ref = {'k': [['r0:returned', 'c1:main:d-', 'a1:main:d-']]}
        for actual in (['r0:returned', 'a1:main:d-', 'c1:main:d-'],
                       ['r0:returned', 'c1:main:d-', 'a1:other:d-'],
                       ['r0:returned', 'c1:main:d-', 'a1:main:throw'],
                       ['r0:returned', 'c1:main:d-']):
            self.assertTrue(run.differences({}, {}, ref, {'k': actual})['timing'])

    def test_all_observed_native_sequences_are_allowed(self):
        first = ['r0:returned', 'c1:main:d-', 'a1:main:d-']
        second = ['r0:returned', 'a2:main:d-', 'c2:main:d-']
        self.assertFalse(any(run.differences({}, {}, {'k': [first, second]}, {'k': second}).values()))

    def test_argument_wording_uses_existing_normalizer_only(self):
        self.assertEqual(run.compare.normalize('bad\nParameter name: buffer'), "bad (Parameter 'buffer')")
        self.assertNotEqual(run.compare.normalize('InvalidOperationException: failed'), 'request')


class CaptureValidationTests(unittest.TestCase):
    def setUp(self):
        root = run.validation_cache()/'runs/gpubuffers-awaitable/offline-unit'
        root.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=root)
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        # These synthetic throws test file validation; they assert no Native behavior.
        self.values = {'complete': True}
        self.timing = {}
        for case in run.CASES:
            self.values.update({case+'.finished': True, case+'.logs': [],
                                case+'.callback.throw': 'synthetic', case+'.async.throw': 'synthetic'})
            self.timing[case] = []

    def write(self):
        for name, rows in (('values', self.values), ('timing', self.timing)):
            (self.path/(name+'.jsonl')).write_text(''.join(json.dumps({'k': k, 'v': v})+'\n' for k, v in rows.items()))

    def test_complete_synthetic_capture_is_read_without_inventing_outcomes(self):
        self.write()
        self.assertEqual(run.validate(self.path), (self.values, self.timing))

    def test_fatal_missing_case_and_extra_case_are_rejected(self):
        for kind in ('fatal', 'missing', 'extra'):
            with self.subTest(kind=kind):
                if kind == 'fatal': self.values['fatal'] = 'synthetic'
                if kind == 'missing': del self.timing['compute']
                if kind == 'extra': self.timing['extra'] = []
                self.write()
                with self.assertRaises(ValueError): run.validate(self.path)
                self.values.pop('fatal', None)
                self.timing['compute'] = []
                self.timing.pop('extra', None)

    def test_duplicate_keys_are_rejected(self):
        self.write()
        with (self.path/'values.jsonl').open('a') as out: out.write('{"k":"complete","v":true}\n')
        with self.assertRaises(SystemExit): run.validate(self.path)

    def test_unfinished_missing_and_contradictory_admission_are_rejected(self):
        for field, value in (('compute.finished', False), ('compute.callback.return', '--'),
                             ('compute.async.returnCompleted', False)):
            old = self.values.copy()
            self.values[field] = value
            self.write()
            with self.assertRaises(ValueError): run.validate(self.path)
            self.values = old

    def test_returned_awaitable_requires_consumption_and_result_fields(self):
        del self.values['compute.async.throw']
        self.values['compute.async.returnCompleted'] = False
        self.write()
        with self.assertRaisesRegex(ValueError, 'consumption outcome'): run.validate(self.path)
        self.values.update({'compute.await.outcome': 'request', 'compute.consumeAgain': 'synthetic'})
        self.write()
        with self.assertRaisesRegex(ValueError, 'missing result field'): run.validate(self.path)

    def test_late_and_malformed_events_are_rejected(self):
        for event in ('a3:main:d-', 'r1:returned', 'bad'):
            self.timing['compute'] = [event]
            self.write()
            with self.assertRaises(ValueError): run.validate(self.path)

    def test_report_does_not_truncate_long_failures(self):
        value = 'full exception '+('x'*2000)
        report = run.differences({'k': value}, {'k': 'other'}, {}, {})
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(run.write_report(self.path/'report.json', report), 1)
        self.assertIn(value, out.getvalue())
        self.assertEqual(json.loads((self.path/'report.json').read_text()), report)

    def test_missing_capture_files_keep_all_missing_values_and_timings(self):
        self.write()
        missing = run.compare_capture(self.path, self.path/'absent')
        self.assertEqual(len(missing['missing']), len(self.values))
        self.assertEqual(len(missing['timing']), len(self.timing))
        self.assertEqual({item['key'] for item in missing['missing']}, set(self.values))
        self.assertEqual({item['key'] for item in missing['timing']}, set(self.timing))

    def test_publish_rejects_one_directory_used_as_both_repeats(self):
        (self.path/'native1').mkdir()
        (self.path/'native2').symlink_to(self.path/'native1', target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'independent'): run.publish(self.path, self.path/'Reference')


if __name__ == '__main__':
    unittest.main()
