import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


PROBE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('oroorder_analyze', PROBE/'analyze.py')
analyze = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyze)


class CompletenessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.reference = analyze.rows(PROBE/'Reference/unity-6000.0.71f1.jsonl')

    def setUp(self):
        self.records = copy.deepcopy(self.reference)
        self.w = analyze.ORO_FIRST + 2

    def problem(self):
        cache = Path.home()/'.cache/unbound/validation/unit/oroorder'
        cache.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=cache) as directory:
            path = Path(directory)/'values.jsonl'
            path.write_text(''.join(json.dumps(r)+'\n' for r in self.records))
            return analyze.guard_problem(path)

    def remove_first(self, predicate):
        index = next(i for i, r in enumerate(self.records) if r.get('w') == self.w and predicate(r))
        return self.records.pop(index)

    def assert_problem_contains(self, text):
        problem = self.problem()
        self.assertIsInstance(problem, str)
        self.assertIn(text, problem)

    def test_complete_reference_passes(self):
        self.assertIsNone(self.problem())

    def test_missing_camera_is_rejected(self):
        self.remove_first(lambda r: r['e'] == 'Camera.onPreCull')
        self.assert_problem_contains('incomplete camera')

    def test_missing_handler_is_rejected(self):
        self.remove_first(lambda r: r['e'].startswith('OnRenderObject cam='))
        self.assert_problem_contains('incomplete or duplicate handlers')

    def test_missing_all_handlers_is_rejected(self):
        self.records = [r for r in self.records if not (r.get('w') == self.w and r['e'].startswith('OnRenderObject cam='))]
        self.assert_problem_contains('handler groups')

    def test_duplicate_handler_is_rejected(self):
        row = next(r for r in self.records if r.get('w') == self.w and r['e'].startswith('OnRenderObject cam='))
        index = self.records.index(row)
        self.records.insert(index, copy.deepcopy(row))
        self.assertIsNotNone(self.problem())

    def test_wrong_camera_group_is_rejected(self):
        row = next(r for r in self.records if r.get('w') == self.w and r['e'].startswith('OnRenderObject cam='))
        row['e'] = 'OnRenderObject cam=UnknownCam'
        self.assert_problem_contains('handler groups')

    def test_duplicate_frame_header_is_rejected(self):
        row = next(r for r in self.records if r.get('w') == self.w and r.get('who') == 'oro' and r['e'].startswith('frame '))
        self.records.insert(self.records.index(row), copy.deepcopy(row))
        self.assert_problem_contains('not unique')

    def test_handler_order_is_not_an_acceptance_guard(self):
        # A counterexample to every prediction remains publishable evidence.
        # Rename the handlers cyclically in every call, preserving membership.
        rename = dict(zip(analyze.HANDLERS, analyze.HANDLERS[1:]+analyze.HANDLERS[:1]))
        for row in self.records:
            if row.get('w', -1) >= analyze.ORO_FIRST and row['e'].startswith('OnRenderObject cam='):
                row['who'] = rename[row['who']]
        self.assertIsNone(self.problem())


class LineComparisonTests(unittest.TestCase):
    def test_shared_diff_keeps_every_missing_and_extra_line(self):
        spec = importlib.util.spec_from_file_location('oro_compare', PROBE/'compare.py')
        comparator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(comparator)
        reference = ['a', 'b', 'b', 'c']
        actual = ['a', 'b', 'x', 'c', 'z']
        rows = comparator.sequence(reference, actual)
        self.assertEqual([r['line'] for r in rows if r['status'] == 'missing'], ['b'])
        self.assertEqual([r['line'] for r in rows if r['status'] == 'extra'], ['x', 'z'])
        self.assertEqual([r['line'] for r in rows if r['status'] == 'pass'], ['a', 'b', 'c'])
        self.assertIs(comparator.sequence, comparator.core_api_compare.compare)

    def test_empty_capture_is_rejected(self):
        spec = importlib.util.spec_from_file_location('oro_compare', PROBE/'compare.py')
        comparator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(comparator)
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)/'empty.jsonl'
            path.write_text('')
            with self.assertRaisesRegex(ValueError, 'empty capture'):
                comparator.lines(path)


if __name__ == '__main__':
    unittest.main()
