#!/usr/bin/env python3
"""The burst-at-duration classifier accepts only its measured shape."""
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


class BurstAtDuration(unittest.TestCase):
    def check(self, key, reference, candidate):
        return compare({key: reference, 'complete': [1]}, {key: candidate, 'complete': [1]})

    def test_accepts_the_last_two_missing(self):
        passing, known, failures = self.check('sim/0/c', record(P, Q, BURST, BURST), record(P, Q))
        self.assertEqual((passing, len(known), failures), (1, 1, []))

    def test_rejects_a_changed_particle(self):
        changed = list(Q)
        changed[1] = 0.6
        _, known, failures = self.check('sim/0/c', record(P, Q, BURST, BURST), record(P, changed))
        self.assertEqual((len(known), len(failures)), (0, 1))

    def test_rejects_another_count(self):
        _, known, failures = self.check('sim/0/c', record(P, Q, BURST, BURST), record(P))
        self.assertEqual((len(known), len(failures)), (0, 1))
        _, known, failures = self.check('sim/0/c', record(P, Q, BURST, BURST), record(P, Q, BURST))
        self.assertEqual((len(known), len(failures)), (0, 1))

    def test_rejects_dropped_particles_older_than_kept_ones(self):
        _, known, failures = self.check('sim/0/c', record(P, Q, Q, Q), record(P, Q))
        self.assertEqual((len(known), len(failures)), (0, 1))

    def test_rejects_other_keys(self):
        _, known, failures = self.check('sim/0/b', record(P, Q, BURST, BURST), record(P, Q))
        self.assertEqual((len(known), len(failures)), (0, 1))
        _, known, failures = self.check('module/main.startSize/const/none', record(P, Q, BURST, BURST), record(P, Q))
        self.assertEqual((len(known), len(failures)), (0, 1))

    def test_missing_and_extra_keys_fail(self):
        passing, known, failures = compare({'a': [1], 'complete': [1]}, {'b': [1], 'complete': [1]})
        self.assertEqual(sorted(f[1] for f in failures), ['extra', 'missing'])


if __name__ == '__main__':
    unittest.main()
