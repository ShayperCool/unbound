import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('assetclone_compare', Path(__file__).with_name('compare.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CompareTests(unittest.TestCase):
    def test_union_retains_missing_extra_and_both_failed_values(self):
        rows = module.compare({'absent': '73', 'differing': 'old'}, {'extra': '81', 'differing': 'new'})
        self.assertEqual({r['key']: r['status'] for r in rows},
                         {'absent': 'missing', 'differing': 'different', 'extra': 'extra'})
        self.assertEqual(next(r for r in rows if r['key'] == 'differing')['reference'], 'old')
        self.assertEqual(next(r for r in rows if r['key'] == 'differing')['actual'], 'new')

    def test_gpu_rounding_does_not_relax_metadata_or_cpu(self):
        a = {'a.gpu.m0.0': '0.5', 'a.cpu.m0.p0.0': '0.5', 'a.width': '100'}
        b = {'a.gpu.m0.0': '0.501', 'a.cpu.m0.p0.0': '0.501', 'a.width': '100.001'}
        self.assertEqual({r['key']: r['status'] for r in module.compare(a, b)},
                         {'a.gpu.m0.0': 'pass', 'a.cpu.m0.p0.0': 'different', 'a.width': 'different'})

    def test_all_passing_rows_are_preserved(self):
        self.assertEqual(len(module.compare({'a': 'x', 'b': 'y'}, {'a': 'x', 'b': 'y'})), 2)


if __name__ == '__main__':
    unittest.main()
