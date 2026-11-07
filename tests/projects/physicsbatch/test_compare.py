import unittest
from compare import compare
from verify import verify


class ComparisonTests(unittest.TestCase):
    def setUp(self):
        self.expected = {'stress/faces/0':'mesh,3,1,2,3,0,0,-1,0',
                         'stress/faces/1':'mesh,6,1,2,6,0,0,-1,1',
                         'stress/faces/2':'none,0,0,0,0,0,0,0,0',
                         'stress/faces/count':'2', 'complete':'yes'}

    def verdict(self, actual):
        report = compare(self.expected, actual)
        return report, verify(self.expected, actual, report)

    def test_permutations_keep_all_strict_failures(self):
        actual = dict(self.expected)
        actual['stress/faces/0'],actual['stress/faces/1'] = actual['stress/faces/1'],actual['stress/faces/0']
        report, verdict = self.verdict(actual)
        self.assertEqual(2,len(report['failures']))
        self.assertEqual(report['failures'],verdict['order_only'])
        self.assertFalse(verdict['unresolved'])
        self.assertEqual(2,verdict['checked_unique_faces'])

    def test_lost_and_duplicate_faces_fail(self):
        actual = dict(self.expected)
        actual['stress/faces/1'] = actual['stress/faces/0']
        self.assertTrue(self.verdict(actual)[1]['unresolved'])
        del actual['stress/faces/1']
        self.assertTrue(self.verdict(actual)[1]['unresolved'])

    def test_geometry_changes_fail(self):
        actual = dict(self.expected)
        actual['stress/faces/0'] = 'mesh,3.01,1,2,3,0,0,-1,0'
        self.assertTrue(self.verdict(actual)[1]['unresolved'])

    def test_hit_after_terminator_fails(self):
        actual = dict(self.expected)
        actual['stress/faces/0'],actual['stress/faces/2'] = actual['stress/faces/2'],actual['stress/faces/0']
        self.assertTrue(self.verdict(actual)[1]['unresolved'])

    def test_unrelated_or_extra_rows_fail(self):
        for key in ['closest/0','stress/faces/99']:
            actual = dict(self.expected, **{key:'extra'})
            self.assertTrue(self.verdict(actual)[1]['unresolved'])


if __name__ == '__main__':
    unittest.main()
