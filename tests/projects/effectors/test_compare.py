import math
import unittest
from compare import contact_order_only, differences, native_outcome


class NativeContactOrderTest(unittest.TestCase):
    def sample(self):
        return {'p': [0, 1], 'normals': [[0, 1], [0, -1]], 'events': ['stay', 'stay']}

    def test_reordering_keeps_the_same_contacts(self):
        a, b = self.sample(), self.sample()
        b['normals'].reverse()
        self.assertTrue(contact_order_only(a, b, list(differences(a, b))))

    def test_duplicate_normal_does_not_replace_a_missing_contact(self):
        a, b = self.sample(), self.sample()
        b['normals'] = [[0, 1], [0, 1]]
        self.assertFalse(native_outcome(a, b))

    def test_extra_contact_fails(self):
        a, b = self.sample(), self.sample()
        b['normals'].append([1, 0])
        self.assertFalse(native_outcome(a, b))

    def test_changed_position_is_not_a_contact_order_difference(self):
        a, b = self.sample(), self.sample()
        b['normals'].reverse(); b['p'][1] += .001
        self.assertFalse(native_outcome(a, b))

    def test_missing_callback_fails(self):
        a, b = self.sample(), self.sample()
        b['normals'].reverse(); b['events'].pop()
        self.assertFalse(native_outcome(a, b))

    def test_unmeasured_overlap_outcome_fails(self):
        a, b = self.sample(), self.sample()
        b['p'][1] = .1
        self.assertFalse(native_outcome(a, b))
        self.assertTrue(native_outcome(b, b))

    def test_nonfinite_normal_fails(self):
        a, b = self.sample(), self.sample()
        b['normals'][0][0] = math.nan
        self.assertFalse(native_outcome(a, b))

    def test_missing_record_fails(self):
        self.assertFalse(native_outcome(self.sample(), 'MISSING'))


if __name__ == '__main__':
    unittest.main()
