import json
from pathlib import Path
import tempfile
import unittest

from compare import rows, entries, compare, check_constructor_stores, validate_controls, CONTROL_VALUES


def value(text):
    return {'status': 'value', 'value': text}


class ComparisonTests(unittest.TestCase):
    def test_recording_controls_enforce_isolation_and_inherited_state(self):
        capture = {key: value(text) for key, text in CONTROL_VALUES.items()}
        validate_controls(capture)
        capture['FreshStateControl/Property/FreshStateControl/ZReads'] = value('1')
        with self.assertRaises(ValueError): validate_controls(capture)
        capture = {key: value(text) for key, text in CONTROL_VALUES.items()}
        capture['FreshStateControl/Property/FreshStateControl/ZNested'] = value('NestedStateDerived{field=2}')
        with self.assertRaises(ValueError): validate_controls(capture)

    def test_exact_debt_does_not_accept_new_values_or_disappear(self):
        expected, actual = {'x': value('1')}, {'x': value('2')}
        debt = {'x': {'expected': value('1'), 'actual': value('2'), 'reason': 'queued'}}
        self.assertFalse(compare(expected, actual, debt)['failures'])
        self.assertEqual(len(compare(expected, {'x': value('3')}, debt)['failures']), 1)
        self.assertEqual(compare(expected, expected, debt)['stale'], ['x'])
        self.assertEqual(compare(expected, {}, debt)['missing'], ['x'])
        self.assertEqual(compare({}, actual, {})['extra'], ['x'])

    def test_variable_rows_preserve_other_fields_and_status(self):
        debt = {'x': {'expectedPattern': r'date:[0-9]+:Local', 'actualPattern': r'date:[0-9]+:Local', 'reason': 'clock'}}
        result = compare({'x': value('date:123:Local')}, {'x': value('date:456:Local')}, debt)
        self.assertFalse(result['failures'])
        self.assertEqual(len(result['differences']), 1)
        self.assertEqual(result['differences'][0]['actual'], value('date:456:Local'))
        self.assertTrue(compare({'x': value('date:123:Local')}, {'x': value('date:456:Utc')}, debt)['failures'])
        self.assertTrue(compare({'x': value('date:123:Local')}, {'x': {'status': 'getter-threw', 'value': 'date:456:Local'}}, debt)['failures'])

    def test_invalid_debt_is_rejected(self):
        with self.assertRaises(ValueError): entries({'release': 'other', 'entries': []}, 'release')
        entry = {'key': 'x', 'reason': ''}
        with self.assertRaises(ValueError): entries({'release': 'r', 'entries': [entry]}, 'r')
        entry['reason'] = 'queued'
        with self.assertRaises(ValueError): entries({'release': 'r', 'entries': [entry, entry]}, 'r')

    def test_truncation_and_duplicate_rows_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'rows.jsonl'
            path.write_text(json.dumps(dict(key='x', **value('1')))+'\n')
            with self.assertRaises(ValueError): rows(path)
            path.write_text(path.read_text()*2+json.dumps(dict(key='complete', **value('1')))+'\n')
            with self.assertRaises(ValueError): rows(path)

    def test_constructor_debt_is_exact_and_detects_fixes(self):
        row = {'constructor': 'C()', 'field': 'x:int', 'expected': 'i4:1', 'actual': 'i4:2'}
        debt = [dict(row, reason='queued')]
        self.assertFalse(check_constructor_stores({'differences': [row]}, debt))
        self.assertTrue(check_constructor_stores({'differences': [dict(row, actual='i4:3')]}, debt))
        self.assertTrue(check_constructor_stores({'differences': []}, debt))


if __name__ == '__main__': unittest.main()
