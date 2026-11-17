"""compare.py --residuals: records keyed by case and index within the case.

    python3 -m unittest discover -s tests/projects/terrain -p test_compare.py
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'compare.py')
RECORDS = [{'case': 'a', 'v': 1.0}, {'case': 'b', 'v': 2.0}, {'case': 'b', 'v': 3.0}]


def run(candidate, listed):
    with tempfile.TemporaryDirectory() as directory:
        for name, records in (('ref', RECORDS), ('cand', candidate)):
            os.makedirs(os.path.join(directory, name))
            with open(os.path.join(directory, name, 'terrain.jsonl'), 'w') as handle:
                handle.write(''.join(json.dumps(r) + '\n' for r in records))
        path = os.path.join(directory, 'residuals.json')
        with open(path, 'w') as handle:
            json.dump(listed, handle)
        done = subprocess.run([sys.executable, SCRIPT, os.path.join(directory, 'ref'), os.path.join(directory, 'cand'),
                               '--residuals', path], capture_output=True, text=True)
        return done.returncode, done.stdout


class TerrainResidualTests(unittest.TestCase):
    def test_a_listed_record_within_its_bound_passes(self):
        candidate = [dict(r) for r in RECORDS]
        candidate[2]['v'] = 3.01
        self.assertEqual(0, run(candidate, {'b[1].v': {'max_abs': 0.02, 'reason': 'test'}})[0])

    def test_the_same_difference_in_another_record_fails(self):
        candidate = [dict(r) for r in RECORDS]
        candidate[1]['v'] = 2.01
        code, out = run(candidate, {'b[1].v': {'max_abs': 0.02, 'reason': 'test'}})
        self.assertEqual(1, code)
        self.assertIn('FAIL b[0].v', out)


if __name__ == '__main__':
    unittest.main()
