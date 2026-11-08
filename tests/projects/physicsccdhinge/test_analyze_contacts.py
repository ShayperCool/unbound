import copy
import unittest
from pathlib import Path

from analyze_contacts import analyze, multiset_equal
from verify_ordering import verify
from replay_compound_order import PREFIX, contacts, replay, matches_body, diagnose
from compare import load


class ContactAnalysisTests(unittest.TestCase):
    def rows(self):
        return {'complete': 'yes', 'case/0/a/event/0': 'enter,b,2,-1,0,0,-2,0,0',
                'case/0/a/event/0/0': '0,0,0,-1,0,0,0.1,a,b',
                'case/0/a/event/0/1': '0,1,0,-1,0,0,0.1,a,b'}

    def test_slot_permutation_preserves_raw_failures(self):
        expected = self.rows()
        actual = self.rows()
        x, y = 'case/0/a/event/0/0', 'case/0/a/event/0/1'
        actual[x], actual[y] = actual[y], actual[x]
        before = copy.deepcopy((expected, actual))
        result = analyze(expected, actual)
        self.assertEqual({'order_only': 2}, result['counts'])
        self.assertEqual(before, (expected, actual))
        self.assertEqual({x, y}, {x['key'] for x in result['failures']})

    def test_changed_normal_or_separation_is_content(self):
        for replacement in ('0,0,0,0,-1,0,0.1,a,b', '0,0,0,-1,0,0,0.2,a,b'):
            actual = self.rows()
            actual['case/0/a/event/0/0'] = replacement
            self.assertEqual({'unmatched_payload': 1}, analyze(self.rows(), actual)['counts'])

    def test_opposite_impulse_is_separate_from_order(self):
        actual = self.rows()
        actual['case/0/a/event/0'] = 'enter,b,2,1,0,0,-2,0,0'
        self.assertEqual({'pair_orientation': 1}, analyze(self.rows(), actual)['counts'])

    def test_alternate_surface_is_not_slot_order(self):
        actual = self.rows()
        actual['case/0/a/event/0/0'] = '-0.1,0,0,-1,0,0,0.1,a,b'
        self.assertEqual({'surface_witness': 1}, analyze(self.rows(), actual)['counts'])

    def test_duplicates_need_distinct_matches(self):
        self.assertFalse(multiset_equal(['a', 'a'], ['a', 'b']))

    def test_lost_contact_stays_visible(self):
        actual = self.rows()
        actual['case/0/a/event/0'] = 'enter,b,1,-1,0,0,-2,0,0'
        del actual['case/0/a/event/0/1']
        result = analyze(self.rows(), actual)
        self.assertEqual({'unmatched_payload': 2}, result['counts'])
        self.assertIsNone(next(x for x in result['failures'] if x['key'].endswith('/1'))['actual'])

    def native_rows(self):
        rows = self.rows()
        rows['case/0/a/events'] = rows['case/0/b/events'] = '1'
        rows['case/0/b/event/0'] = 'enter,a,2,-1,0,0,2,0,0'
        for i in range(2):
            rows['case/0/b/event/0/'+str(i)] = f'0,{i},0,1,0,0,0.1,b,a'
            rows['case/0/native/0/'+str(i)] = f'0,{i},0,-1,0,0,0.1,-0.5,0,0'
        rows['case/0/native/0'] = 'a/a,b/b,True,False,False,2,-1,0,0'
        return {key.replace('case/', 'ordering/'): value for key, value in rows.items()}

    def test_native_impulse_reaches_both_receivers_in_pair_direction(self):
        rows = self.native_rows()
        self.assertEqual([], verify(rows, rows)['unresolved'])
        actual = dict(rows)
        actual['ordering/0/b/event/0'] = 'enter,a,2,1,0,0,2,0,0'
        self.assertTrue(verify(rows, actual)['unresolved'])

    def test_native_missing_point_is_not_accepted_as_order(self):
        rows = self.native_rows()
        actual = dict(rows)
        del actual['ordering/0/native/0/1']
        self.assertTrue(verify(rows, actual)['unresolved'])

    def test_removing_native_report_is_detected(self):
        rows = self.native_rows()
        actual = {key: value for key, value in rows.items() if '/native/' not in key}
        self.assertTrue(verify(rows, actual)['unresolved'])

    def test_normal_response_reconstructs_independent_editor_body_reading(self):
        rows = load(Path(__file__).parent/'Reference/unity-6000.0.71f1/geometry.tsv')
        self.assertTrue(matches_body(replay(contacts(rows)), rows[PREFIX+'/body']))

    def test_middle_pair_exchange_changes_motion_magnitude_and_direction(self):
        rows = load(Path(__file__).parent/'Reference/unity-6000.0.71f1/geometry.tsv')
        inputs = contacts(rows)
        original = replay(inputs)
        inputs[1], inputs[2] = inputs[2], inputs[1]
        changed = replay(inputs)
        self.assertGreater(original['velocity'][1], 0)
        self.assertLess(changed['velocity'][1], 0)
        self.assertGreater(abs(original['velocity'][0]-changed['velocity'][0]), .2)

    def test_normal_response_covers_all_seven_measured_contexts(self):
        rows = load(Path(__file__).parent/'Reference/unity-6000.0.71f1/geometry.tsv')
        result = diagnose(rows, rows)
        self.assertEqual(7, len(result['cases']))
        self.assertTrue(result['reference_reconstructed'])
        self.assertTrue(result['engine_reconstructed'])


if __name__ == '__main__':
    unittest.main()
