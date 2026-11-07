import copy
from pathlib import Path
import unittest
from compare import read, compare


class ComparatorTests(unittest.TestCase):
    def setUp(self):
        self.reference = read(Path(__file__).parent/'Reference/unity-6000.0.71f1/xvfb/values.jsonl')
        self.candidate = copy.deepcopy(self.reference)

    def test_matching_record(self):
        self.assertEqual([], compare(self.reference, self.candidate))

    def test_missing_extra_and_raw_failures_remain_visible(self):
        del self.candidate['clipboard.empty']
        self.candidate['extra'] = True
        self.candidate['clipboard.external'] = 'wrong'
        errors = compare(self.reference, self.candidate)
        self.assertEqual(3, len(errors))
        self.assertTrue(any('clipboard.empty' in error for error in errors))
        self.assertTrue(any('extra' in error for error in errors))
        self.assertTrue(any('clipboard.external' in error for error in errors))

    def test_window_offset_is_allowed_but_displacement_is_checked(self):
        for key in self.candidate:
            if key.startswith('warp.'):
                self.candidate[key] = [value + 100 for value in self.candidate[key]]
        self.assertEqual([], compare(self.reference, self.candidate))
        self.candidate['warp.240.legacy'][0] += 2
        self.assertTrue(compare(self.reference, self.candidate))

    def test_wrong_host_ratio_and_density_fail(self):
        self.candidate['mainWindowDisplayInfo'][4] = 1000
        self.candidate['dpi'] = 96
        self.assertEqual(3, len(compare(self.reference, self.candidate)))

    def test_nonfinite_pointer_fails(self):
        self.candidate['warp.240.legacy'] = [float('nan'), float('nan')]
        self.candidate['warp.240.mouse'] = self.candidate['warp.240.legacy']
        self.assertTrue(compare(self.reference, self.candidate))


if __name__ == '__main__':
    unittest.main()
