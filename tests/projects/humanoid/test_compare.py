#!/usr/bin/env python3
"""Self-test of compare.py's bounded known classes: a covered value within its bound is known, one
beyond it and a sum beyond its bound are differences, and an uncovered value keeps the tolerance.

    python3 -m unittest discover -s tests/projects/humanoid -p test_compare.py
"""
import contextlib
import io
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import compare  # noqa: E402

KNOWN = {'bounded': {'reason': 'test', 'bounds': [
    {'key': r'^pose\.step\d+$', 'field': r'^muscles\[(0|1)\]$', 'within': 1e-2},
    {'key': r'^locals\.step\d+$', 'field': r'^Arm\[1\]$', 'within': 1e-2}],
    'sums': [{'key': r'^pose\.step\d+$', 'fields': ['muscles[0]', 'muscles[1]'], 'within': 1e-4}]}}


def run(reference, actual):
    with tempfile.TemporaryDirectory() as directory:
        paths = []
        for name, rows in (('ref', reference), ('act', actual), ('known', None)):
            path = os.path.join(directory, name + '.json')
            with open(path, 'w') as handle:
                if rows is None:
                    json.dump(KNOWN, handle)
                else:
                    handle.writelines(json.dumps({'k': k, 'v': v}) + '\n' for k, v in rows.items())
            paths.append(path)
        summary = os.path.join(directory, 'summary.json')
        argv = sys.argv
        sys.argv = ['compare.py', paths[0], paths[1], '-q', '--known', paths[2], '--json', summary]
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                code = compare.main()
        finally:
            sys.argv = argv
        with open(summary) as handle:
            return code, json.load(handle)


class BoundedClassTests(unittest.TestCase):
    reference = {'pose.step0': {'muscles': [0.5, -0.5, 0.1]},
                 'locals.step0': {'Arm': [[0, 0, 0], [0.5, 0.5, 0.5, 0.5]]}}

    def test_a_split_within_its_bounds_is_known(self):
        code, summary = run(self.reference, {'pose.step0': {'muscles': [0.505, -0.505, 0.1]},
                                             'locals.step0': {'Arm': [[0, 0, 0], [-0.505, -0.5, -0.5, -0.5]]}})
        self.assertEqual(code, 0)
        self.assertEqual(len(summary['known']), 3)
        self.assertEqual(summary['differences'], [])

    def test_values_beyond_their_bound_and_a_sum_beyond_its_bound_differ(self):
        code, summary = run(self.reference, {'pose.step0': {'muscles': [0.52, -0.52, 0.1]},
                                             'locals.step0': {'Arm': [[0, 0, 0], [0.53, 0.5, 0.5, 0.5]]}})
        self.assertEqual(code, 1)
        self.assertEqual(sum('beyond the bound' in line for line in summary['differences']), 3)
        code, summary = run(self.reference, {'pose.step0': {'muscles': [0.5005, -0.5, 0.1]},
                                             'locals.step0': self.reference['locals.step0']})
        self.assertEqual(code, 1)
        self.assertTrue(any('sum beyond the bound' in line for line in summary['differences']))

    def test_an_uncovered_value_keeps_the_tolerance(self):
        code, summary = run(self.reference, {'pose.step0': {'muscles': [0.5, -0.5, 0.105]},
                                             'locals.step0': self.reference['locals.step0']})
        self.assertEqual(code, 1)
        self.assertEqual(len(summary['differences']), 1)
        self.assertNotIn('beyond the bound', summary['differences'][0])


if __name__ == '__main__':
    unittest.main()
