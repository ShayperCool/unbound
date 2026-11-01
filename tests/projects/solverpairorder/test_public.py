"""The public CLI must distinguish a retained numeric failure from lost observations."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class PublicComparisonTests(unittest.TestCase):
    def compare(self, baseline, candidate):
        cache = Path(os.environ.get('XDG_CACHE_HOME', Path.home()/'.cache'))/'unbound'
        cache.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='pair-comparator-', dir=cache) as temp:
            work = Path(temp)
            for name, data in [('reference', {'case': {'x': 1, 'y': 2}}),
                               ('baseline', baseline), ('candidate', candidate)]:
                (work/(name+'.json')).write_text(json.dumps(data))
            result = subprocess.run([
                sys.executable, str(Path(__file__).with_name('public.py')),
                '--release', '6000.0.71f1', '--reference', str(work/'reference.json'),
                '--baseline', str(work/'baseline.json'), '--candidate', str(work/'candidate.json'),
                '--output', str(work/'report.json')], capture_output=True, text=True)
            return result.returncode, json.loads((work/'report.json').read_text())

    def test_retained_numeric_failure_cannot_hide_a_missing_field(self):
        code, report = self.compare({'case': {'x': 9, 'y': 2}}, {'case': {'y': 2}})
        self.assertEqual(code, 1)
        self.assertEqual(report['retained'], [['case', 'x']])
        self.assertEqual([(x['scenario'], x['key']) for x in report['newly_missing']], [('case', 'x')])

    def test_missing_scenario_retains_every_lost_observation(self):
        code, report = self.compare({'case': {'x': 9, 'y': 2}}, {})
        self.assertEqual(code, 1)
        self.assertEqual(len(report['newly_missing']), 2)
        self.assertEqual(len(report['failures']), 2)

    def test_existing_numeric_failure_remains_a_retained_failure(self):
        code, report = self.compare({'case': {'x': 9, 'y': 2}}, {'case': {'x': 9, 'y': 2}})
        self.assertEqual(code, 0)
        self.assertEqual(report['retained'], [['case', 'x']])
        self.assertEqual(report['newly_missing'], [])

    def test_already_missing_field_is_still_reported(self):
        code, report = self.compare({'case': {'y': 2}}, {'case': {'y': 2}})
        self.assertEqual(code, 0)
        self.assertEqual(len(report['failures']), 1)
        self.assertEqual(report['newly_missing'], [])


if __name__ == '__main__':
    unittest.main()
