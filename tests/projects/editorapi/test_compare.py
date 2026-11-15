import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('editorapi_run', Path(__file__).with_name('run.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class ExactGapTests(unittest.TestCase):
    def check(self, actual, gaps, expected):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            reference, observed, report = root/'reference.jsonl', root/'actual.jsonl', root/'report.json'
            reference.write_text(json.dumps({'key': 'value', 'value': 'native'})+'\n')
            observed.write_text(''.join(json.dumps({'key': key, 'value': value})+'\n' for key, value in actual.items()))
            reference.with_name('known-gaps.json').write_text(json.dumps(gaps))
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(expected, runner.compare_with_gaps(reference, observed, report))
            return json.loads(report.read_text())

    def gap(self):
        return [{'key': 'value', 'reference': 'native', 'actual': 'pending', 'reason': 'producer follow-up'}]

    def test_exact_queued_difference_stays_in_raw_report(self):
        result = self.check({'value': 'pending'}, self.gap(), 0)
        self.assertEqual(0, result['matched'])
        self.assertEqual(1, len(result['failures']))
        self.assertEqual(1, len(result['queued']))

    def test_improvement_makes_gap_stale(self):
        result = self.check({'value': 'native'}, self.gap(), 1)
        self.assertEqual(1, len(result['stale']))

    def test_changed_actual_and_extra_rows_fail(self):
        result = self.check({'value': 'wrong', 'extra': 'pending'}, self.gap(), 1)
        self.assertEqual(2, len(result['unexpected']))

    def test_missing_row_fails(self):
        self.check({}, self.gap(), 1)

    def test_changed_reference_cannot_use_old_gap(self):
        gaps = self.gap()
        gaps[0]['reference'] = 'old'
        self.check({'value': 'pending'}, gaps, 1)

    def test_no_gap_is_needed_for_equal_rows(self):
        self.check({'value': 'native'}, [], 0)

    def test_empty_reason_and_duplicate_keys_are_rejected(self):
        for gaps in (self.gap()*2, [dict(self.gap()[0], reason='')]):
            with self.assertRaises(ValueError):
                self.check({'value': 'pending'}, gaps, 0)
