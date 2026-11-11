"""Completeness controls for wind reference publication."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('wind_run', Path(__file__).with_name('run.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.result = {'exit_code': 0, 'forced_exit': None}
        self.rows = [{'case': 'defaults'}]
        self.rows += [{'case': 'scenario', 'name': str(i), 'steps': [None]*5} for i in range(14)]
        self.rows += [{'case': 'pulse', 'samples': [[0, 1], [1, 1]]} for _ in range(3)]

    def check(self):
        (self.directory/'editor-run.json').write_text(json.dumps(self.result))
        (self.directory/'wind.jsonl').write_text('\n'.join(map(json.dumps, self.rows))+'\n')
        return runner.complete(self.directory)

    def test_complete_record(self):
        self.assertIsNone(self.check())

    def test_missing_last_pulse_is_rejected(self):
        self.rows.pop()
        self.assertIsNotNone(self.check())

    def test_duplicate_scenario_is_rejected(self):
        self.rows[2]['name'] = self.rows[1]['name']
        self.assertIsNotNone(self.check())

    def test_empty_pulse_is_rejected(self):
        self.rows[-1]['samples'] = []
        self.assertIsNotNone(self.check())

    def test_partial_simulation_is_rejected(self):
        self.rows[1]['steps'].pop()
        self.assertIsNotNone(self.check())

    def test_failed_and_forced_exits_are_rejected(self):
        self.result['exit_code'] = 1
        self.assertIsNotNone(self.check())
        self.result.update(exit_code=0, forced_exit='shutdown-guard')
        self.assertIsNotNone(self.check())
