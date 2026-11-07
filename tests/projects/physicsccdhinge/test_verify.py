import unittest
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'physicsbatch'))
from compare import compare
from verify_results import verify


class VerificationTests(unittest.TestCase):
    def result(self, expected, actual):
        return verify(expected, actual, compare(expected, actual))

    def test_only_angle_roundoff_is_admitted(self):
        key = 'hinge/motor/0/hinge'
        expected = {key:'0.13705,360,0,0,0,0,0,0'}
        actual = {key:'0.13122,360,0,0,0,0,0,0'}
        self.assertEqual(1, len(self.result(expected, actual)['angle_roundoff']))
        actual[key] = '0.12,360,0,0,0,0,0,0'
        self.assertEqual(1, len(self.result(expected, actual)['unresolved']))
        actual[key] = '0.13122,350,0,0,0,0,0,0'
        self.assertEqual(1, len(self.result(expected, actual)['unresolved']))

    def test_wrap_roundoff_and_missing_reading(self):
        key = 'hinge/limits/0/hinge'
        expected = {key:'-179.99998,360,0,0,0,0,0,0'}
        actual = {key:'180,360,0,0,0,0,0,0'}
        self.assertEqual(1, len(self.result(expected, actual)['angle_roundoff']))
        self.assertEqual(1, len(self.result(expected, {})['unresolved']))

    def test_pair_orientation_requires_every_other_field(self):
        key = 'ccd/test/0/event/0'
        expected = {key:'stay,wall,1,65,0,0,-100,0,0',key+'/0':'0,0,0,-1,0,0,.8,a,wall'}
        actual = dict(expected, **{key:'stay,wall,1,-65,0,0,-100,0,0'})
        self.assertEqual(1, len(self.result(expected, actual)['pair_orientation']))
        actual[key] = 'stay,wall,1,-60,0,0,-100,0,0'
        self.assertEqual(1, len(self.result(expected, actual)['unresolved']))
        actual[key] = 'stay,wall,1,-65,0,0,-90,0,0'
        self.assertEqual(1, len(self.result(expected, actual)['unresolved']))
        actual[key] = 'stay,wall,1,-65,0,0,-100,0,0'
        del actual[key+'/0']
        self.assertEqual(2, len(self.result(expected, actual)['unresolved']))

    def test_resting_velocity_roundoff_is_bounded(self):
        key = 'hinge/limits/0/hinge'
        expected = {key:'90,0,0,0,0,0,0,0'}
        actual = {key:'90,0.000342,0,0,0,0,0,0'}
        self.assertEqual(1, len(self.result(expected, actual)['resting_velocity_roundoff']))
        actual[key] = '90,0.001,0,0,0,0,0,0'
        self.assertEqual(1, len(self.result(expected, actual)['unresolved']))
        actual[key] = '89,0.000342,0,0,0,0,0,0'
        self.assertEqual(1, len(self.result(expected, actual)['unresolved']))


if __name__ == '__main__':
    unittest.main()
