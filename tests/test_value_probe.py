"""value_probe.same: numbers within the tolerance, equal numbers and NaNs match.

    python3 tests/test_value_probe.py
"""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
from value_probe import same  # noqa: E402

INF = float('inf')
NAN = float('nan')


class SameTests(unittest.TestCase):
    def test_numbers_within_the_tolerance(self):
        self.assertTrue(same(1.0, 1.0001, 2e-4))
        self.assertFalse(same(1.0, 1.001, 2e-4))
        self.assertTrue(same(3, 3.0, 0))

    def test_equal_infinities_match_and_others_do_not(self):
        self.assertTrue(same(INF, INF, 2e-4))
        self.assertTrue(same(-INF, -INF, 0))
        self.assertTrue(same([[INF, INF, INF], [0, 0, 0]], [[INF, INF, INF], [0, 0, 0]], 2e-4))
        self.assertFalse(same(INF, -INF, 2e-4))
        self.assertFalse(same(INF, 3.4e38, 2e-4))
        self.assertFalse(same(INF, NAN, 2e-4))

    def test_nan_matches_nan_only(self):
        self.assertTrue(same(NAN, NAN, 0))
        self.assertFalse(same(NAN, 0.0, 1.0))

    def test_booleans_take_no_tolerance(self):
        self.assertFalse(same(True, False, 1.0))
        self.assertTrue(same(False, False, 0))


if __name__ == '__main__':
    unittest.main()
