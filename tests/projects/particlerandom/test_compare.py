"""Guard identity classification without changing the strict comparison or its tolerances."""
import copy
import json
import unittest
from pathlib import Path

import compare


class BirthLifetimeClassificationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with (Path(__file__).parent/'Reference/unity-6000.0.71f1.jsonl').open() as handle:
            reference = {row['key']: row['value'] for row in map(json.loads, handle)}
        cls.key = 'birthlife/batch/nothing/child/1'
        cls.expected = reference[cls.key]

    def permuted(self):
        particles = [self.expected[i:i + compare.PARTICLE_FIELDS]
                     for i in range(1, len(self.expected), compare.PARTICLE_FIELDS)]
        return [self.expected[0]] + sum(particles[1:] + particles[:1], [])

    def test_permutation_is_classified_but_strict_failures_remain(self):
        actual = self.permuted()
        self.assertTrue(compare.retained_birth_lifetime_permutation(self.key, self.expected, actual))
        self.assertTrue(any(not compare.near(self.key, i, x, y)
                            for i, (x, y) in enumerate(zip(self.expected, actual))))

    def test_every_particle_field_is_checked(self):
        for field in range(compare.PARTICLE_FIELDS):
            with self.subTest(field=field):
                actual = self.permuted()
                actual[1 + field] += 1
                self.assertFalse(compare.retained_birth_lifetime_permutation(self.key, self.expected, actual))

    def test_duplicate_lost_and_extra_particles_are_rejected(self):
        duplicate = self.permuted()
        duplicate[34:67] = duplicate[1:34]
        lost = self.permuted()[:-compare.PARTICLE_FIELDS]
        extra = self.permuted() + self.expected[1:34]
        for actual in (duplicate, lost, extra):
            self.assertFalse(compare.retained_birth_lifetime_permutation(self.key, self.expected, actual))
        # Matching duplicate multiplicities do not authorize collapsing identities.
        self.assertFalse(compare.retained_birth_lifetime_permutation(self.key, duplicate, copy.copy(duplicate)))

    def test_count_and_context_are_checked(self):
        actual = self.permuted()
        actual[0] -= 1
        self.assertFalse(compare.retained_birth_lifetime_permutation(self.key, self.expected, actual))
        self.assertFalse(compare.retained_birth_lifetime_permutation('birthlife/other/child/1',
                                                                    self.expected, self.permuted()))



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
