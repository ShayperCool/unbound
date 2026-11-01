import unittest

from raw_delta import union


class RawDeltaTests(unittest.TestCase):
    def test_union_keeps_missing_extra_and_baseline_only_rows(self):
        rows = union({'case': {'lost': 1, 'retained': 2}},
                     {'case': {'lost': 3, 'retained': 2, 'removed_extra': 4}},
                     {'case': {'retained': 2, 'new_extra': 5}})
        self.assertEqual([row['key'] for row in rows],
                         ['lost', 'new_extra', 'removed_extra', 'retained'])
        self.assertTrue(rows[0]['reference_present'])
        self.assertTrue(rows[0]['baseline_present'])
        self.assertFalse(rows[0]['candidate_present'])
        self.assertFalse(rows[1]['reference_present'])
        self.assertFalse(rows[2]['candidate_present'])
        self.assertFalse(rows[3]['changed'])

    def test_raw_error_increases_have_no_tolerance_or_rounding_mask(self):
        rows = union({'case': {'tiny': 1, 'improved': 0, 'flag': True}},
                     {'case': {'tiny': 1, 'improved': 2, 'flag': False}},
                     {'case': {'tiny': 1.00000001, 'improved': 1, 'flag': True}})
        by_key = {row['key']: row for row in rows}
        self.assertTrue(by_key['tiny']['error_increased'])
        self.assertGreater(by_key['tiny']['candidate_error'], 0)
        self.assertFalse(by_key['improved']['error_increased'])
        self.assertNotIn('baseline_error', by_key['flag'])


if __name__ == '__main__':
    unittest.main()
