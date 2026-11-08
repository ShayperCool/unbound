import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'physicsbatch'))
from compare import compare
from verify_geometry import verify


class GeometryVerificationTests(unittest.TestCase):
    def result(self, expected, actual):
        return verify(expected, actual, compare(expected, actual))

    def test_motion_and_callback_counts_still_gate(self):
        key = 'geometry/static/1/0/2/0/bullet'
        expected = {key+'/body':'-.125,0,0,0,0,0,1,0,0,0,0,0,0', key+'/events':'0'}
        actual = dict(expected)
        actual[key+'/body'] = '1,0,0,0,0,0,1,100,0,0,0,0,0'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected)
        actual[key+'/events'] = '1'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        del actual[key+'/body']
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def test_contact_trace_is_reported_but_cannot_be_truncated(self):
        key = 'geometry/static/1/0/3/0/bullet'
        expected = {key+'/events':'1', key+'/event/0':'enter,wall,1,1,0,0,-100,0,0',
                    key+'/event/0/0':'0,0,0,-1,0,0,.8,bullet,wall'}
        actual = dict(expected)
        actual[key+'/event/0/0'] = '0,.1,0,-1,0,0,.8,bullet,wall'
        result = self.result(expected, actual)
        self.assertFalse(result['unresolved'])
        self.assertEqual(1, len(result['contact_trace']))
        del actual[key+'/event/0/0']
        self.assertTrue(self.result(expected, actual)['unresolved'])
        del actual[key+'/event/0']
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def test_reflection_requires_every_other_body_field(self):
        key = 'geometry/kinematic/2/1/3/0/target/body'
        expected = {key:'1,.1,0,0,0,0,1,68,5,0,0,0,0'}
        actual = {key:'1,-.1,0,0,0,0,1,68,-5,0,0,0,0'}
        self.assertEqual(1, len(self.result(expected, actual)['mirrored_motion']))
        actual[key] = '1,-.1,0,0,0,0,1,60,-5,0,0,0,0'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        outside = 'geometry/kinematic/1/1/3/0/target/body'
        self.assertTrue(self.result({outside:expected[key]}, {outside:actual[key]})['unresolved'])

    def test_reflection_must_be_consistent_for_every_body_and_step(self):
        prefix = 'geometry/dynamic/3/0/3'
        a, b = prefix+'/0/a/body', prefix+'/0/b/body'
        expected = {a:'-.3,.1,0,0,0,0,1,36,3,0,0,0,0', b:'.3,-.1,0,0,0,0,1,-36,-3,0,0,0,0'}
        actual = dict(expected)
        actual[a] = '-.3,-.1,0,0,0,0,1,36,-3,0,0,0,0'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual[b] = '.3,.1,0,0,0,0,1,-36,3,0,0,0,0'
        self.assertFalse(self.result(expected, actual)['unresolved'])
        expected[prefix+'/1/a/body'] = expected[a]
        actual[prefix+'/1/a/body'] = expected[a]
        self.assertTrue(self.result(expected, actual)['unresolved'])


if __name__ == '__main__':
    unittest.main()
