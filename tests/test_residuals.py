"""residuals.py: loading residual lists and the gate verdict.

    python3 -m unittest discover -s tests -p test_residuals.py
"""
import json
import os
import tempfile
import unittest

import residuals


class LoadTests(unittest.TestCase):
    def write(self, value):
        handle = tempfile.NamedTemporaryFile('w', suffix='.json', delete=False)
        json.dump(value, handle)
        handle.close()
        self.addCleanup(os.unlink, handle.name)
        return handle.name

    def test_no_list_is_empty(self):
        self.assertEqual({}, residuals.load(None))

    def test_entries_need_a_reason_and_a_bound(self):
        for bad in ([], {'a': 1}, {'a': {'max_abs': 1}}, {'a': {'reason': ' '}}, {'a': {'reason': 'r'}}):
            with self.assertRaises(residuals.ResidualListError, msg=bad):
                residuals.load(self.write(bad))
        self.assertEqual({'a': {'max_abs': 1, 'reason': 'r'}}, residuals.load(self.write({'a': {'max_abs': 1, 'reason': 'r'}})))


class GateTests(unittest.TestCase):
    LIST = {'a': {'max_abs': 0.5, 'reason': 'one step'}, 'b': {'actual': 'x', 'reason': 'text'}}

    def test_numbers_are_bounded_and_other_measures_equal(self):
        gate = residuals.Gate(self.LIST)
        self.assertTrue(gate.differs('a', {'max_abs': 0.5}))
        self.assertTrue(gate.differs('b', {'actual': 'x'}))
        self.assertFalse(gate.differs('b', {'actual': 'y'}))
        gate.finish(['a', 'b'])
        self.assertFalse(gate.ok)

    def test_beyond_a_bound_or_an_unbounded_measure_fails(self):
        for measured in ({'max_abs': 0.6}, {'max_abs': float('nan')}, {'max_abs': True}, {'max_abs': 0.1, 'pixels': 1}, {}):
            gate = residuals.Gate(self.LIST)
            self.assertFalse(gate.differs('a', measured), measured)

    def test_an_unlisted_difference_fails(self):
        gate = residuals.Gate(self.LIST)
        self.assertFalse(gate.differs('c', {'max_abs': 0}))
        self.assertIn('FAIL c: max_abs 0 (not recorded)', gate.lines())

    def test_now_matching_and_stale_entries(self):
        gate = residuals.Gate(self.LIST)
        gate.exact_key('a')
        gate.exact_key('z')
        gate.finish(['a', 'z'], noun='image')
        lines = gate.lines()
        self.assertIn('NOW MATCHES a: its recorded residual can be withdrawn', lines)
        self.assertIn('FAIL b: a recorded residual for an image the reference does not have', lines)
        self.assertEqual('gate: 2 exact, 0 within recorded residuals, 1 failed', lines[-1])
        self.assertFalse(gate.ok)

    def test_missing_keys_fail_even_when_listed(self):
        gate = residuals.Gate(self.LIST)
        gate.missing('a', 'missing')
        gate.finish(['a', 'b'])
        self.assertIn('FAIL a: missing', gate.lines())


if __name__ == '__main__':
    unittest.main()
