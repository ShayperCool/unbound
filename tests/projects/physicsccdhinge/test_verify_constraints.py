import copy
import unittest

from verify_constraints import verify


class ConstraintVerificationTests(unittest.TestCase):
    def rows(self, mask):
        prefix = f'constraints/{mask}/True/0/bullet'
        return {prefix+'/body': '-1,0,0,0,0,0,1,100,0,0,0,0,0',
                prefix+'/events': '1',
                prefix+'/event/0': 'enter,wall,1,-1,0,0,-100,0,0',
                prefix+'/event/0/0': '0,0,0,-1,0,0,0.1,bullet,wall'}

    def test_constrained_motion_remains_strict(self):
        expected = self.rows(16)
        actual = dict(expected)
        key = next(k for k in actual if k.endswith('/body'))
        actual[key] = actual[key].replace('-1,0,0', '-2,0,0', 1)
        self.assertTrue(verify(expected, actual)['unresolved'])

    def test_position_lock_motion_remains_strict(self):
        expected = self.rows(4)
        actual = dict(expected)
        key = next(k for k in actual if k.endswith('/body'))
        actual[key] = actual[key].replace('-1,0,0', '-1.0002,0,0', 1)
        self.assertTrue(verify(expected, actual)['unresolved'])

    def test_negative_control_trace_is_retained_without_mutating_it(self):
        expected = self.rows(0)
        actual = dict(expected)
        key = next(k for k in actual if k.endswith('/body'))
        actual[key] = actual[key].replace('-1,0,0', '-1.0002,0,0', 1)
        before = copy.deepcopy((expected, actual))
        result = verify(expected, actual)
        self.assertFalse(result['unresolved'])
        self.assertEqual([key], [x['key'] for x in result['free_rotation_trace']])
        self.assertEqual(before, (expected, actual))

    def test_negative_controls_still_require_counts_identities_and_finite_values(self):
        expected = self.rows(4)
        for suffix, value in [('/events', '2'),
                              ('/event/0', 'enter,other,1,-1,0,0,-100,0,0'),
                              ('/event/0/0', 'nan,0,0,-1,0,0,0.1,bullet,wall'),
                              ('/event/0/0', '0,0,0,-1,0,0,0.1,other,wall')]:
            actual = dict(expected)
            key = next(k for k in actual if k.endswith(suffix))
            actual[key] = value
            self.assertTrue(verify(expected, actual)['unresolved'], value)

    def test_missing_control_is_rejected(self):
        expected = self.rows(16)
        actual = dict(expected)
        del actual[next(k for k in actual if k.endswith('/body'))]
        self.assertTrue(verify(expected, actual)['unresolved'])
