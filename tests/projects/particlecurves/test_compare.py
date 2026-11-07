#!/usr/bin/env python3
"""The comparator is strict: every key and value within the particle probes' bound."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from compare import compare  # noqa: E402

P = [0.5, 0.75, 4, 3.76, 1, 0]
Q = [0.5, 0.5, 4, 3.26, 1, 0]
BURST = [1, 0, 4, 3.76015377, 1, 0]


def record(*particles):
    return [len(particles)] + [v for p in particles for v in p]


class Strict(unittest.TestCase):
    def check(self, key, reference, candidate):
        return compare({key: reference, 'complete': [1]}, {key: candidate, 'complete': [1]})

    def test_equal_records_pass(self):
        passing, failures = self.check('sim/0/c', record(P, Q, BURST, BURST), record(P, Q, BURST, BURST))
        self.assertEqual((passing, failures), (2, []))

    def test_missing_burst_particles_fail(self):
        # The burst at the loop end is no longer a known difference.
        _, failures = self.check('sim/0/c', record(P, Q, BURST, BURST), record(P, Q))
        self.assertEqual(len(failures), 1)

    def test_a_value_outside_the_bound_fails(self):
        changed = list(Q)
        changed[3] += 0.002
        _, failures = self.check('sim/0/b', record(P, Q), record(P, changed))
        self.assertEqual(len(failures), 1)
        changed[3] = Q[3] + 0.0005
        _, failures = self.check('sim/0/b', record(P, Q), record(P, changed))
        self.assertEqual(failures, [])

    def test_missing_and_extra_keys_fail(self):
        _, failures = compare({'a': [1], 'complete': [1]}, {'b': [1], 'complete': [1]})
        self.assertEqual(sorted(f[1] for f in failures), ['extra', 'missing'])


if __name__ == '__main__':
    unittest.main()
