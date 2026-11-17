"""compare.py --residuals: rounding tokens bounded, every other differing token pinned.

    python3 -m unittest discover -s tests/projects/lightprobes -p test_compare_residuals.py
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'compare.py')


class ResidualTests(unittest.TestCase):
    REFERENCE = {'query 0': '85 -0.16 0.058 0.5', 'occlusion': '1 1 0.9999999 1', 'same': '1 2'}
    LIST = {'query 0': {'rounding_tokens': 1, 'max_abs': 1.2e-7, 'pinned': '0=32', 'reason': 'test'},
            'occlusion': {'rounding_tokens': 0, 'max_abs': 0, 'pinned': '1=0', 'reason': 'test'}}

    def run_compare(self, candidate, listed=LIST):
        with tempfile.TemporaryDirectory() as directory:
            paths = []
            for name, values in (('reference', self.REFERENCE), ('candidate', candidate)):
                path = os.path.join(directory, name + '.jsonl')
                with open(path, 'w') as handle:
                    for key, value in values.items():
                        handle.write(json.dumps({'key': key, 'value': value}) + '\n')
                paths.append(path)
            residuals = os.path.join(directory, 'residuals.json')
            with open(residuals, 'w') as handle:
                json.dump(listed, handle)
            done = subprocess.run([sys.executable, SCRIPT, *paths, '--residuals', residuals], capture_output=True, text=True)
            return done.returncode, done.stdout

    def test_the_recorded_differences_pass(self):
        code, out = self.run_compare({'query 0': '32 -0.16 0.05800006 0.5', 'occlusion': '1 0 0.9999999 1', 'same': '1 2'})
        self.assertEqual(0, code, out)

    def test_a_larger_float_beside_a_pinned_index_fails(self):
        code, out = self.run_compare({'query 0': '32 20 0.05800006 0.5', 'occlusion': '1 0 0.9999999 1', 'same': '1 2'})
        self.assertEqual(1, code, out)
        self.assertIn('FAIL query 0', out)

    def test_another_occlusion_channel_fails(self):
        code, out = self.run_compare({'query 0': '32 -0.16 0.05800006 0.5', 'occlusion': '1 0 0 1', 'same': '1 2'})
        self.assertEqual(1, code, out)
        self.assertIn('FAIL occlusion', out)

    def test_an_unlisted_key_fails(self):
        code, out = self.run_compare({'query 0': '32 -0.16 0.05800006 0.5', 'occlusion': '1 0 0.9999999 1', 'same': '1 3'})
        self.assertEqual(1, code, out)
        self.assertIn('FAIL same', out)


if __name__ == '__main__':
    unittest.main()
