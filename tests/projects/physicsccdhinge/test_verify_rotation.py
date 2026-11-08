import copy
import unittest

from compare import compare
from verify_rotation import verify


class RotationVerificationTests(unittest.TestCase):
    def rows(self, control=0, locked='False'):
        prefix = f'rotation/contact/{control}/{locked}/True/0/bullet'
        return {prefix+'/body': '-1,0,0,0,0,0,1,100,0,0,0,0,0',
                prefix+'/events': '1',
                prefix+'/event/0': 'enter,wall,1,-1,0,0,-100,0,0',
                prefix+'/event/0/0': '0,0,0,-1,0,0,0.1,bullet,wall'}

    def check(self, expected, actual):
        return verify(expected, actual, compare(expected, actual))

    def test_flight_and_configuration_remain_strict(self):
        for key in ['rotation/flight/1/True/0/bullet/body',
                    'rotation/contact/0/False/True/properties',
                    'rotation/contact/3/False/True/tuning']:
            self.assertTrue(self.check({key: '1'}, {key: '2'})['unresolved'])

    def test_primitive_explicit_inertia_and_position_locks_remain_strict(self):
        for control, locked in [(1, 'False'), (2, 'False'), (0, 'True'), (3, 'True')]:
            expected = self.rows(control, locked)
            actual = dict(expected)
            key = next(k for k in actual if k.endswith('/body'))
            actual[key] = actual[key].replace('-1,0,0', '-1.0002,0,0', 1)
            self.assertTrue(self.check(expected, actual)['unresolved'])

    def test_diagnostic_trace_is_preserved(self):
        expected = self.rows()
        actual = dict(expected)
        key = next(k for k in actual if k.endswith('/body'))
        actual[key] = actual[key].replace('100,0,0,0,0,0', '100,0,0,0.0002,0,0', 1)
        before = copy.deepcopy((expected, actual))
        result = self.check(expected, actual)
        self.assertFalse(result['unresolved'])
        self.assertEqual([key], [x['key'] for x in result['numeric_diagnostics']])
        self.assertEqual(before, (expected, actual))

    def test_diagnostics_require_complete_finite_rows_counts_and_identities(self):
        expected = self.rows()
        for suffix, value in [('/events', '2'),
                              ('/event/0', 'exit,wall,1,-1,0,0,-100,0,0'),
                              ('/event/0', 'enter,other,1,-1,0,0,-100,0,0'),
                              ('/event/0', 'enter,wall,2,-1,0,0,-100,0,0'),
                              ('/event/0/0', 'nan,0,0,-1,0,0,0.1,bullet,wall'),
                              ('/event/0/0', '0,0,0,-1,0,0,0.1,other,wall'),
                              ('/body', '-2,0,0,0,0,0,1,100,0,0,0,0,0'),
                              ('/body', '0'),
                              ('/body', None)]:
            actual = dict(expected)
            key = next(k for k in actual if k.endswith(suffix))
            if value is None:
                del actual[key]
            else:
                actual[key] = value
            self.assertTrue(self.check(expected, actual)['unresolved'], value)
