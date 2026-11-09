"""Guard identity classification without changing the strict comparison or its tolerances."""
import copy
import json
import unittest
from pathlib import Path

import compare


class LifetimeRowTests(unittest.TestCase):
    key = 'model/inherit/play/const/const/lifetime/lifetimes'
    row = [2, 1234, 5, 0.25, 4321, 6, 0.5]

    def test_seed_halves_are_exact_and_lifetimes_use_the_tolerance(self):
        for index in (1, 2, 4, 5):
            with self.subTest(index=index):
                self.assertFalse(compare.near(self.key, index, self.row[index], self.row[index] + 1e-6))
        for index in (3, 6):
            with self.subTest(index=index):
                self.assertTrue(compare.near(self.key, index, self.row[index], self.row[index] + 1e-6))
                self.assertFalse(compare.near(self.key, index, self.row[index], self.row[index] + 1e-3))
        self.assertFalse(compare.near(self.key, 0, 2, 3))

if __name__ == '__main__':
    unittest.main()
